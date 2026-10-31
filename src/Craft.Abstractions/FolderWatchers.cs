// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

namespace Craft.Abstractions;

/// <summary>What happened to an entry of a watched folder.</summary>
public enum FolderChangeKind
{
    /// <summary>A file or folder was made.</summary>
    Created,

    /// <summary>A file or folder was removed.</summary>
    Deleted,

    /// <summary>A file or folder was renamed (<see cref="FolderChange.OldPath"/> is its old path).</summary>
    Renamed,

    /// <summary>The watcher lost track (its buffer overflowed, or it failed): everything shown is to be read again.</summary>
    Rescan,
}

/// <summary>One change of a watched folder.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Path">The entry's full path (for <see cref="FolderChangeKind.Rescan"/>, the watched folder).</param>
/// <param name="OldPath">A renamed entry's old full path, else null.</param>
public readonly record struct FolderChange(FolderChangeKind Kind, string Path, string? OldPath = null);

/// <summary>
/// Tells the host when entries of a folder (and of the folders below it) are made, removed or renamed, registered in
/// <see cref="FolderWatchers"/>: the folder tree follows the disk through it. The library's "system" is .NET's
/// <see cref="FileSystemWatcher"/> on every platform; "none" never reports (the tree changes on Refresh and on the
/// editor's own saves only). A file's content changing is not a change here.
/// </summary>
public interface IFolderWatcher : IKind
{
    /// <summary>
    /// Starts watching <paramref name="folder"/> and every folder below it: <paramref name="changed"/> is called on a
    /// background thread (never the UI thread), possibly from several at once, for each change. Dispose the result to stop.
    /// </summary>
    /// <exception cref="IOException">The folder cannot be watched here (the message says why).</exception>
    IDisposable Watch(string folder, Action<FolderChange> changed);
}

/// <summary>
/// The folder watchers, by name (ignoring case): the library's <see cref="System"/> and <see cref="None"/>; a platform or
/// a plug-in may register another. <c>CRAFT_WORKSPACE_WATCHER</c> picks one; an unknown name fails with "not registered".
/// </summary>
public static class FolderWatchers
{
    /// <summary>.NET's <see cref="FileSystemWatcher"/> (file and folder names, every folder below).</summary>
    public const string System = "system";

    /// <summary>A watcher that never reports.</summary>
    public const string None = "none";

    private static readonly KindTable<IFolderWatcher> Table = BuiltIn();

    private static KindTable<IFolderWatcher> BuiltIn()
    {
        var table = new KindTable<IFolderWatcher>(nameof(FolderWatchers), "Folder watcher", "add it with FolderWatchers.Register",
            comparer: StringComparer.OrdinalIgnoreCase, unguarded: "a watcher is asked once, when a folder opens");
        table.RegisterDefault(new SystemFolderWatcher());
        table.RegisterDefault(new NoFolderWatcher());
        return table;
    }

    /// <summary>Registers <paramref name="watcher"/> under its name (over the library's of that name, which stays behind it).</summary>
    public static void Register(IFolderWatcher watcher)
    {
        ArgumentNullException.ThrowIfNull(watcher);
        Table.Register(watcher, global::System.Reflection.Assembly.GetCallingAssembly());
    }

    /// <summary>Removes the app's watcher of that name (the library's, if any, answers again).</summary>
    public static bool Unregister(string name) => Table.Unregister(name);

    /// <summary>The registered names, in order.</summary>
    public static IReadOnlyList<string> Names => Table.Names;

    /// <summary>The watcher of that name, or null.</summary>
    public static IFolderWatcher? Find(ReadOnlySpan<char> name) => Table.Find(name);

    /// <summary>The watcher of that name.</summary>
    /// <exception cref="NotRegisteredException">None is registered under it.</exception>
    public static IFolderWatcher Get(ReadOnlySpan<char> name) => Table.Get(name);
}

/// <summary>
/// "system": a <see cref="FileSystemWatcher"/> over the folder and its subfolders, file and folder names only (a write to
/// a file's content is not reported). Its buffer is the largest the platform takes without paging (64 KB); when it
/// overflows anyway (a large checkout), one <see cref="FolderChangeKind.Rescan"/> is reported instead of the lost events.
/// </summary>
internal sealed class SystemFolderWatcher : IFolderWatcher
{
    private const int BufferBytes = 64 * 1024;

    public string Name => FolderWatchers.System;

    public string Summary => ".NET's FileSystemWatcher: file and folder names, every folder below";

    public IDisposable Watch(string folder, Action<FolderChange> changed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        ArgumentNullException.ThrowIfNull(changed);
        var watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
            InternalBufferSize = BufferBytes,
        };
        watcher.Created += (_, e) => changed(new FolderChange(FolderChangeKind.Created, e.FullPath));
        watcher.Deleted += (_, e) => changed(new FolderChange(FolderChangeKind.Deleted, e.FullPath));
        watcher.Renamed += (_, e) => changed(new FolderChange(FolderChangeKind.Renamed, e.FullPath, e.OldFullPath));
        watcher.Error += (_, _) => changed(new FolderChange(FolderChangeKind.Rescan, folder));
        watcher.EnableRaisingEvents = true;
        return watcher;
    }
}

/// <summary>"none": never reports.</summary>
internal sealed class NoFolderWatcher : IFolderWatcher
{
    public string Name => FolderWatchers.None;

    public string Summary => "no watching: the tree changes on Refresh and on the editor's own saves";

    public IDisposable Watch(string folder, Action<FolderChange> changed) => new Nothing();

    private sealed class Nothing : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
