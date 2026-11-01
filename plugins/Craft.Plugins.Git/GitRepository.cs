// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using System.Text;
using Craft.Abstractions;

namespace Craft.Plugins.Git;

/// <summary>A git command that failed: its words (the first line of what git said).</summary>
public sealed class GitException(string message) : Exception(message);

/// <summary>
/// A git repository (X part 8) on the user's own git: its status read with <c>status --porcelain=v2 -z</c> and parsed as
/// bytes, read again after a pause (<see cref="ScheduleRefresh"/>, <c>refreshMs</c>) or at once (<see cref="RefreshAsync"/>),
/// one read at a time (a request during a read reads once more after it); <see cref="Changed"/> is raised on the UI thread
/// only when git's output changed. Every command runs off the calling thread.
/// </summary>
public sealed class GitRepository : ISourceControlRepository, IDisposable
{
    private readonly GitProgram _git;
    private readonly Action<Action> _dispatch;
    private readonly Func<int> _refreshDelay;
    private readonly Lock _gate = new();
    private SourceControlStatus _status = SourceControlStatus.None;
    private byte[] _lastOutput = [];
    private Task? _reading;
    private bool _again;
    private CancellationTokenSource? _pause;
    private bool _disposed;

    /// <summary>The repository at <paramref name="root"/> run by <paramref name="git"/>; <see cref="Changed"/> is raised through <paramref name="dispatch"/>.</summary>
    public GitRepository(GitProgram git, string root, Action<Action> dispatch, Func<int>? refreshDelay = null)
    {
        _git = git ?? throw new ArgumentNullException(nameof(git));
        _dispatch = dispatch ?? throw new ArgumentNullException(nameof(dispatch));
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
        _refreshDelay = refreshDelay ?? (() => 500);
    }

    /// <inheritdoc />
    public string Provider => GitProvider.ProviderName;

    /// <inheritdoc />
    public string Root { get; }

    /// <summary>The program it runs.</summary>
    public GitProgram Program => _git;

    /// <inheritdoc />
    public SourceControlStatus Status => Volatile.Read(ref _status);

    /// <summary>How many times the status was read (a test counts the debounce).</summary>
    public int Reads { get; private set; }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>The repository <paramref name="folder"/> is in (its top level), or null when it is in none.</summary>
    public static async Task<string?> FindRootAsync(GitProgram git, string folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(git);
        var result = await git.RunAsync(folder, ["rev-parse", "--show-toplevel"], null, cancellationToken).ConfigureAwait(false);
        var text = result.Text.Trim();
        return result.Ok && text.Length > 0 ? Path.GetFullPath(text) : null;
    }

    /// <summary>Reads the status again after the pause (the folder's changes, a save); a read asked again meanwhile waits its pause again.</summary>
    public void ScheduleRefresh()
    {
        CancellationTokenSource pause;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pause?.Cancel();
            _pause?.Dispose();
            _pause = pause = new CancellationTokenSource();
        }

        _ = PauseThenRefreshAsync(pause.Token);
    }

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return Task.CompletedTask;
            }

            if (_reading is { IsCompleted: false } running)
            {
                _again = true;                                   // once more after the read in flight
                return running;
            }

            _reading = Task.Run(ReadLoopAsync, CancellationToken.None);
            return _reading;
        }
    }

    /// <inheritdoc />
    public async Task StageAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            return;
        }

        await RunAsync(["add", "-A", "--", .. Relative(paths)], cancellationToken).ConfigureAwait(false);
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UnstageAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (paths.Count == 0)
        {
            return;
        }

        var relative = Relative(paths);
        var result = await _git.RunAsync(Root, ["restore", "--staged", "--", .. relative], null, cancellationToken).ConfigureAwait(false);
        if (!result.Ok)
        {
            // Before the first commit there is no HEAD to restore from: the files leave the index instead.
            await RunAsync(["rm", "--cached", "-r", "-q", "--", .. relative], cancellationToken).ConfigureAwait(false);
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task CommitAsync(string message, bool amend, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        List<string> arguments = ["commit", "--cleanup=strip", "-F", "-"];
        if (amend)
        {
            arguments.Add("--amend");
        }

        await RunAsync(arguments, cancellationToken, Encoding.UTF8.GetBytes(message)).ConfigureAwait(false);
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<string?> ReadStagedAsync(string path, CancellationToken cancellationToken = default) => ShowAsync(":" + RelativeOf(path), cancellationToken);

    /// <inheritdoc />
    public Task<string?> ReadCommittedAsync(string path, CancellationToken cancellationToken = default) => ShowAsync("HEAD:" + RelativeOf(path), cancellationToken);

    /// <summary>The staged blob of <paramref name="path"/> as bytes, or null when it is not in the index.</summary>
    public async Task<byte[]?> ReadStagedBytesAsync(string path, CancellationToken cancellationToken = default)
    {
        var result = await _git.RunAsync(Root, ["show", ":" + RelativeOf(path)], null, cancellationToken).ConfigureAwait(false);
        return result.Ok ? result.Output : null;
    }

    /// <summary>
    /// Puts <paramref name="text"/> in the index as <paramref name="path"/>'s staged content (a hunk staged: the index's text
    /// with that hunk taken from the working copy), keeping the file's mode.
    /// </summary>
    public async Task StageTextAsync(string path, string text, CancellationToken cancellationToken = default)
    {
        string relative = RelativeOf(path);
        var listed = await RunAsync(["ls-files", "-s", "--", relative], cancellationToken).ConfigureAwait(false);
        string mode = listed.Text.Split(' ', 2)[0] is { Length: 6 } m ? m : "100644";
        var hash = await RunAsync(["hash-object", "-w", "--stdin", "--path=" + relative], cancellationToken, Encoding.UTF8.GetBytes(text)).ConfigureAwait(false);
        await RunAsync(["update-index", "--add", "--cacheinfo", $"{mode},{hash.Text.Trim()},{relative}"], cancellationToken).ConfigureAwait(false);
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Discards the working copy's changes of <paramref name="files"/>: a tracked file takes its staged content back, an
    /// untracked one is deleted. What each held before is given back (its bytes, or null when it did not exist), for Undo.
    /// </summary>
    public async Task<IReadOnlyList<(string Path, byte[]? Bytes)>> DiscardAsync(IReadOnlyList<SourceControlFile> files, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(files);
        var kept = new List<(string, byte[]?)>(files.Count);
        var tracked = new List<string>();
        foreach (var file in files)
        {
            kept.Add((file.Path, File.Exists(file.Path) ? await File.ReadAllBytesAsync(file.Path, cancellationToken).ConfigureAwait(false) : null));
            if (file.State == SourceControlState.Untracked)
            {
                File.Delete(file.Path);
            }
            else
            {
                tracked.Add(file.Path);
            }
        }

        if (tracked.Count > 0)
        {
            await RunAsync(["checkout", "--", .. Relative(tracked)], cancellationToken).ConfigureAwait(false);
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        return kept;
    }

    /// <summary>The local branches, with their upstreams and how far they are from them.</summary>
    public async Task<IReadOnlyList<GitBranchInfo>> BranchesAsync(CancellationToken cancellationToken = default)
    {
        var result = await _git.RunAsync(Root, ["for-each-ref", "--format=%(refname:short)%09%(upstream:short)%09%(upstream:track)", "refs/heads"], null, cancellationToken).ConfigureAwait(false);
        return result.Ok ? GitParsers.Branches(result.Output) : [];
    }

    /// <summary>The repository's last <paramref name="count"/> subjects, newest first (none before the first commit).</summary>
    public async Task<IReadOnlyList<string>> SubjectsAsync(int count, CancellationToken cancellationToken = default)
    {
        if (count <= 0)
        {
            return [];
        }

        var result = await _git.RunAsync(Root, ["log", "-n", count.ToString(System.Globalization.CultureInfo.InvariantCulture), "--format=%s"], null, cancellationToken).ConfigureAwait(false);
        return result.Ok ? result.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
    }

    /// <summary>
    /// The changes a commit message is written from as a unified diff's bytes: the staged ones, else (when nothing is
    /// staged) every change of the working copy with the untracked files as new files; and whether it is every change.
    /// </summary>
    public async Task<(byte[] Diff, bool AllChanges)> CommitDiffAsync(CancellationToken cancellationToken = default)
    {
        var staged = await RunAsync(["diff", "--cached", "--no-ext-diff", "--no-color"], cancellationToken).ConfigureAwait(false);
        if (staged.Output.Length > 0)
        {
            return (staged.Output, false);
        }

        var working = await RunAsync(["diff", "--no-ext-diff", "--no-color"], cancellationToken).ConfigureAwait(false);
        var all = new MemoryStream();
        all.Write(working.Output);
        foreach (var file in Status.Changes)
        {
            if (file.State == SourceControlState.Untracked)
            {
                all.Write(Encoding.UTF8.GetBytes($"diff --git a/{file.RelativePath} b/{file.RelativePath}\nnew file\n"));
                if (File.Exists(file.Path) && new FileInfo(file.Path).Length <= 64 * 1024)
                {
                    foreach (var line in (await File.ReadAllTextAsync(file.Path, cancellationToken).ConfigureAwait(false)).Split('\n'))
                    {
                        all.Write(Encoding.UTF8.GetBytes("+" + line.TrimEnd('\r') + "\n"));
                    }
                }
            }
        }

        return (all.ToArray(), true);
    }

    /// <summary>Runs git with <paramref name="arguments"/> in the repository; a failure throws <see cref="GitException"/> with git's words.</summary>
    public async Task<GitResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default, byte[]? input = null)
    {
        var result = await _git.RunAsync(Root, arguments, input, cancellationToken).ConfigureAwait(false);
        if (!result.Ok)
        {
            throw new GitException($"git {arguments[0]}: {result.Problem}");
        }

        return result;
    }

    /// <summary>The path of <paramref name="path"/> relative to the root, with '/'.</summary>
    public string RelativeOf(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Path.GetRelativePath(Root, Path.GetFullPath(path)).Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>Whether <paramref name="path"/> is inside the repository.</summary>
    public bool Contains(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var full = Path.GetFullPath(path);
        return full.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(Root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _pause?.Cancel();
            _pause?.Dispose();
            _pause = null;
        }
    }

    private async Task PauseThenRefreshAsync(CancellationToken pause)
    {
        try
        {
            await Task.Delay(Math.Max(0, _refreshDelay()), pause).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await RefreshAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task ReadLoopAsync()
    {
        while (true)
        {
            lock (_gate)
            {
                _again = false;
            }

            await ReadOnceAsync().ConfigureAwait(false);
            lock (_gate)
            {
                if (!_again || _disposed)
                {
                    return;
                }
            }
        }
    }

    private async Task ReadOnceAsync()
    {
        GitResult result;
        try
        {
            result = await _git.RunAsync(Root, ["--no-optional-locks", "status", "--porcelain=v2", "-z", "--branch", "--show-stash", "--untracked-files=all"]).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return;                                               // git went away: the view says so on its next action
        }

        Reads++;
        if (!result.Ok || result.Output.AsSpan().SequenceEqual(_lastOutput))
        {
            return;
        }

        _lastOutput = result.Output;
        var status = GitParsers.Status(Root, result.Output);
        Volatile.Write(ref _status, status);
        _dispatch(() => Changed?.Invoke(this, EventArgs.Empty));
    }

    private async Task<string?> ShowAsync(string spec, CancellationToken cancellationToken)
    {
        var result = await _git.RunAsync(Root, ["show", spec], null, cancellationToken).ConfigureAwait(false);
        return result.Ok ? Encoding.UTF8.GetString(result.Output) : null;
    }

    private List<string> Relative(IReadOnlyList<string> paths)
    {
        var relative = new List<string>(paths.Count);
        foreach (var path in paths)
        {
            relative.Add(RelativeOf(path));
        }

        return relative;
    }
}

/// <summary>"git" in <see cref="SourceControlProviders"/>: the user's own git program opens the repository a folder is in.</summary>
/// <param name="settings">The plug-in's settings now (the program's path, the pause).</param>
/// <param name="dispatch">Runs an action on the host's UI thread (the repositories' <see cref="ISourceControlRepository.Changed"/>).</param>
public sealed class GitProvider(Func<GitSettings> settings, Action<Action> dispatch) : ISourceControlProvider
{
    /// <summary>The provider's name.</summary>
    public const string ProviderName = "git";

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public string Summary => "the user's own git program (PATH or CRAFT_PLUGIN_GIT_PATH), its output parsed as bytes";

    /// <inheritdoc />
    public string? Unavailable
    {
        get
        {
            GitProgram.Find(settings().Path, out var why);
            return why;
        }
    }

    /// <inheritdoc />
    public async Task<ISourceControlRepository?> OpenAsync(string folder, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var current = settings();
        if (GitProgram.Find(current.Path, out _) is not { } git)
        {
            return null;
        }

        var root = await GitRepository.FindRootAsync(git, folder, cancellationToken).ConfigureAwait(false);
        if (root is null)
        {
            return null;
        }

        var repository = new GitRepository(git, root, dispatch, () => settings().RefreshMilliseconds);
        await repository.RefreshAsync(cancellationToken).ConfigureAwait(false);
        return repository;
    }
}
