//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RoutingSlips;

/// <summary>
/// One reversible step in a routing slip. An activity has exactly two operations: execute, and
/// compensate the effects of a successful execution. The executor resolves the activity from DI
/// for each attempt, so activities are stateless and all durable state lives in the arguments.
/// </summary>
/// <typeparam name="TArguments">The strongly typed slip arguments.</typeparam>
public interface IActivity<in TArguments> where TArguments : class {
    /// <summary>Runs the step.</summary>
    /// <param name="arguments">The shared slip arguments; mutate them with any state a later step or compensation needs.</param>
    /// <param name="cancellationToken">Cancels the step.</param>
    Task ExecuteAsync(TArguments arguments, CancellationToken cancellationToken);

    /// <summary>Undoes the effects of a successful <see cref="ExecuteAsync"/>.</summary>
    /// <param name="arguments">The same arguments instance the execution observed.</param>
    /// <param name="cancellationToken">Cancels the compensation.</param>
    Task CompensateAsync(TArguments arguments, CancellationToken cancellationToken);
}
