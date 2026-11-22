//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

namespace Jeninnet.CQRS.RequestResponse;

/// <summary>Process-local waiter for one pending request; never persisted.</summary>
internal abstract class RequestWaiter {
    internal abstract RequestResponseMode Mode { get; }

    internal abstract Type ResponseType { get; }

    internal abstract void Complete(object response);

    internal abstract IReadOnlyList<object> Snapshot();
}

internal sealed class FirstRequestWaiter : RequestWaiter {
    private readonly TaskCompletionSource<object> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal override RequestResponseMode Mode => RequestResponseMode.First;

    internal override Type ResponseType { get; }

    internal FirstRequestWaiter(Type responseType) => ResponseType = responseType;

    internal Task<object> Task => _completion.Task;

    internal override void Complete(object response) => _completion.TrySetResult(response);

    internal override IReadOnlyList<object> Snapshot() =>
        _completion.Task.Status is TaskStatus.RanToCompletion ? [_completion.Task.Result] : [];
}

internal sealed class AllRequestWaiter : RequestWaiter {
    private readonly Lock _sync = new();
    private readonly List<object> _responses = [];

    internal override RequestResponseMode Mode => RequestResponseMode.All;

    internal override Type ResponseType { get; }

    internal AllRequestWaiter(Type responseType) => ResponseType = responseType;

    internal override void Complete(object response) {
        lock(_sync) {
            _responses.Add(response);
        }
    }

    internal override IReadOnlyList<object> Snapshot() {
        lock(_sync) {
            return [.. _responses];
        }
    }
}

/// <summary>Process-local registry connecting responses to their awaiting client.</summary>
internal sealed class RequestWaiterRegistry {
    private readonly ConcurrentDictionary<string, RequestWaiter> _waiters = new(StringComparer.Ordinal);

    internal void Add(string requestId, RequestWaiter waiter) {
        if(!_waiters.TryAdd(requestId, waiter)) {
            throw new InvalidOperationException($"A waiter for request '{requestId}' is already registered.");
        }
    }

    internal bool TryGet(string requestId, out RequestWaiter waiter) =>
        _waiters.TryGetValue(requestId, out waiter!);

    internal void Remove(string requestId) => _waiters.TryRemove(requestId, out _);
}
