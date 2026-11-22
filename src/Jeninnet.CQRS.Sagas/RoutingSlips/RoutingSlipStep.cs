//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RoutingSlips;

/// <summary>Read-only description of one configured slip step.</summary>
/// <param name="Name">The stable step id used by progress and inspection.</param>
/// <param name="ActivityType">The activity implementation type.</param>
/// <param name="Options">The validated execution and compensation policy.</param>
public sealed record RoutingSlipStepInfo(string Name, Type ActivityType, RoutingSlipStepOptions Options);

/// <summary>One typed step held by a definition; the executor resolves the activity per attempt.</summary>
/// <param name="Name">The stable step id.</param>
/// <param name="ActivityType">The activity implementation type.</param>
/// <param name="Options">The frozen policy snapshot.</param>
internal sealed record RoutingSlipStep<TArguments>(string Name, Type ActivityType, RoutingSlipStepOptions Options)
    where TArguments : class {
    internal RoutingSlipStepInfo Info => new(Name, ActivityType, Options);
}
