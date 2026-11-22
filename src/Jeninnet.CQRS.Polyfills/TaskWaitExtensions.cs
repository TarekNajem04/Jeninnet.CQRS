//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
#if NETSTANDARD2_0 || NET462
namespace System.Threading.Tasks;

/// <summary>netstandard2.0 stand-in for the cancellable Task.WaitAsync overloads.</summary>
internal static class TaskWaitExtensions {
    /// <summary>Waits for the task or throws when the token is cancelled.</summary>
    /// <param name="task">The task to await.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    public static async Task WaitAsync(this Task task, CancellationToken cancellationToken) {
        if(task is null) {
            throw new ArgumentNullException(nameof(task));
        }

        if(task.IsCompleted || !cancellationToken.CanBeCanceled) {
            await task.ConfigureAwait(false);

            return;
        }

        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using(cancellationToken.Register(static state => ((TaskCompletionSource<bool>)state).TrySetResult(true), cancelled)) {
            if(await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false) != task) {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        await task.ConfigureAwait(false);
    }

    /// <summary>Waits for the task or throws when the timeout elapses.</summary>
    /// <param name="task">The task to await.</param>
    /// <param name="timeout">The maximum wait.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    public static async Task WaitAsync(this Task task, TimeSpan timeout, CancellationToken cancellationToken) {
        if(task is null) {
            throw new ArgumentNullException(nameof(task));
        }

        var delay = Task.Delay(timeout, cancellationToken);

        if(await Task.WhenAny(task, delay).ConfigureAwait(false) != task) {
            cancellationToken.ThrowIfCancellationRequested();

            throw new TimeoutException($"The task did not complete within {timeout}.");
        }

        await task.ConfigureAwait(false);
    }

    /// <summary>Waits for the result or throws when the token is cancelled.</summary>
    /// <typeparam name="TResult">The task result type.</typeparam>
    /// <param name="task">The task to await.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    public static async Task<TResult> WaitAsync<TResult>(this Task<TResult> task, CancellationToken cancellationToken) {
        if(task is null) {
            throw new ArgumentNullException(nameof(task));
        }

        if(task.IsCompleted || !cancellationToken.CanBeCanceled) {
            return await task.ConfigureAwait(false);
        }

        var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using(cancellationToken.Register(static state => ((TaskCompletionSource<bool>)state).TrySetResult(true), cancelled)) {
            if(await Task.WhenAny(task, cancelled.Task).ConfigureAwait(false) != task) {
                throw new OperationCanceledException(cancellationToken);
            }
        }

        return await task.ConfigureAwait(false);
    }
}
#endif