//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sample.AotSmoke;

/// <summary>A void command staged by one process and replayed by another.</summary>
public sealed record AotDeferredCommand(string Name) : ICommand;

/// <summary>A result command staged by one process and replayed by another.</summary>
public sealed record AotDeferredResultCommand(int Value) : ICommand<int>;

/// <summary>Records what the replayed handlers actually received.</summary>
public sealed class AotReplayProbe {
    private readonly Lock _sync = new();
    private readonly List<string> _received = [];
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Gets a task that completes when both deferred commands ran.</summary>
    public Task Completion => _completion.Task;

    /// <summary>Gets the received messages, in execution order.</summary>
    public string Summary {
        get {
            lock(_sync) {
                return string.Join(", ", _received);
            }
        }
    }

    /// <summary>Gets whether the replayed payloads survived serialization unchanged.</summary>
    public bool Succeeded {
        get {
            lock(_sync) {
                return _received is ["greet:AOT", "result:42"];
            }
        }
    }

    /// <summary>Records one handler execution.</summary>
    /// <param name="value">The observed value.</param>
    public void Record(string value) {
        lock(_sync) {
            _received.Add(value);

            if(_received.Count >= 2) {
                _completion.TrySetResult();
            }
        }
    }
}

/// <summary>Void handler: proves a <c>DeferredEnvelope&lt;T&gt;</c> rehydrated after restart.</summary>
public sealed class AotDeferredCommandHandler(AotReplayProbe probe) : ICommandHandler<AotDeferredCommand> {
    public Task<Unit> HandleAsync(AotDeferredCommand command, CancellationToken cancellationToken) {
        probe.Record($"greet:{command.Name}");

        return Task.FromResult(Unit.Value);
    }
}

/// <summary>Result handler: proves a <c>DeferredResultEnvelope&lt;T, TResult&gt;</c> rehydrated too.</summary>
public sealed class AotDeferredResultCommandHandler(AotReplayProbe probe) : ICommandHandler<AotDeferredResultCommand, int> {
    public Task<int> HandleAsync(AotDeferredResultCommand command, CancellationToken cancellationToken) {
        var result = command.Value * 2;
        probe.Record($"result:{result}");

        return Task.FromResult(result);
    }
}