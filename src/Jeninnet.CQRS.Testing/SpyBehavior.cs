//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Testing;

/// <summary>
/// A call log shared with <see cref="SpyBehavior{TRequest, TResponse}"/>.
/// </summary>
public sealed class BehaviorCallLog {
    /// <summary>Gets the recorded behavior executions, in order.</summary>
    public List<string> Calls { get; } = [];
}

/// <summary>
/// A pipeline behavior that records its own execution into a <see cref="BehaviorCallLog"/>.
/// Register it in test DI to prove pipeline traversal with
/// <see cref="CqrsTestAssertions.ShouldHaveRunBehavior{TBehavior}(BehaviorCallLog, string?)"/>.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
/// <remarks>
/// Initializes a new instance with the given call log.
/// </remarks>
/// <param name="log">The log receiving one entry per execution. Never <see langword="null"/>.</param>
public sealed class SpyBehavior<TRequest, TResponse>(BehaviorCallLog log) : IPipelineBehavior<TRequest, TResponse> {
    private readonly BehaviorCallLog _log = log ?? throw new ArgumentNullException(nameof(log));

    /// <inheritdoc />
    public Task<TResponse> HandleAsync(TRequest request, Func<TRequest, CancellationToken, Task<TResponse>> next, CancellationToken cancellationToken) {
        _log.Calls.Add(typeof(SpyBehavior<TRequest, TResponse>).FullName ?? nameof(SpyBehavior<,>));
        return next(request, cancellationToken);
    }
}