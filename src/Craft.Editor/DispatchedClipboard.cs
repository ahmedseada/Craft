// Copyright (c) 2026 Ahmed Seada
// Licensed under the Apache License, Version 2.0. See LICENSE in the repository root.

using Craft.Editor.Abstractions;

namespace Craft.Editor;

/// <summary>
/// The clipboard as every Craft view reaches it (the owner's note 20): each call to the platform's clipboard is posted
/// through the UI dispatcher, so it runs on the UI thread and never inside the key or pointer handler that asked for it
/// (WinUI's clipboard called from within an input callback, or off the UI thread, can fail natively, where no managed
/// handler sees it), and whatever the platform throws, at once or later, comes back as the task's failure and is
/// reported to <c>failed</c>; nothing escapes into the dispatcher's callback.
/// </summary>
public sealed class DispatchedClipboard : IEditorClipboard
{
    private readonly IEditorClipboard _platform;
    private readonly Action<Action> _post;
    private readonly Action<Exception>? _failed;

    /// <summary>The <paramref name="platform"/>'s clipboard reached through <paramref name="post"/> (the UI dispatcher's post); a failure goes to <paramref name="failed"/> too.</summary>
    public DispatchedClipboard(IEditorClipboard platform, Action<Action> post, Action<Exception>? failed = null)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(post);
        _platform = platform;
        _post = post;
        _failed = failed;
    }

    /// <inheritdoc />
    public Task<string?> GetTextAsync() => Run(_platform.GetTextAsync);

    /// <inheritdoc />
    public Task SetTextAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return Run(async () =>
        {
            await _platform.SetTextAsync(text).ConfigureAwait(false);
            return (string?)null;
        });
    }

    private Task<string?> Run(Func<Task<string?>> call)
    {
        var done = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _post(() =>
            {
                try
                {
                    call().ContinueWith(static (task, state) =>
                    {
                        var (owner, done) = ((DispatchedClipboard, TaskCompletionSource<string?>))state!;
                        if (task.IsFaulted)
                        {
                            owner.Fail(done, task.Exception!.GetBaseException());
                        }
                        else if (task.IsCanceled)
                        {
                            done.TrySetCanceled();
                        }
                        else
                        {
                            done.TrySetResult(task.Result);
                        }
                    }, (this, done), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
#pragma warning disable CA1031 // The platform's failure, whatever it is, is the task's: it never escapes the dispatcher's callback.
                catch (Exception error)
#pragma warning restore CA1031
                {
                    Fail(done, error);
                }
            });
        }
#pragma warning disable CA1031 // A dispatcher that refuses the post (the window closing) fails the task, not the caller.
        catch (Exception error)
#pragma warning restore CA1031
        {
            Fail(done, error);
        }

        return done.Task;
    }

    private void Fail(TaskCompletionSource<string?> done, Exception error)
    {
        try
        {
            _failed?.Invoke(error);
        }
#pragma warning disable CA1031 // A report that fails must not hide the failure it reports.
        catch (Exception)
#pragma warning restore CA1031
        {
        }

        done.TrySetException(error);
    }
}
