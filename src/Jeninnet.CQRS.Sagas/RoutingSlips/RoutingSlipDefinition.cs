//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RoutingSlips;

/// <summary>
/// A configured routing slip that can start a new execution or resume a persisted one. The typed
/// definition owns the activity types and arguments type; the executor owns the loop.
/// </summary>
public interface IRoutingSlipDefinition {
    /// <summary>Gets the stable definition name.</summary>
    string Name { get; }

    /// <summary>Gets the strongly typed arguments type.</summary>
    Type ArgumentsType { get; }

    /// <summary>Gets the ordered step descriptions.</summary>
    IReadOnlyList<RoutingSlipStepInfo> Steps { get; }

    /// <summary>Starts a new slip.</summary>
    /// <param name="executor">The executor that owns persistence and policy.</param>
    /// <param name="arguments">The typed arguments instance.</param>
    /// <param name="slipId">An explicit slip id, or <see langword="null"/> for a generated one.</param>
    /// <param name="cancellationToken">Cancels the execution.</param>
    Task<RoutingSlipResult> ExecuteAsync(
        RoutingSlipExecutor executor,
        object arguments,
        string? slipId,
        CancellationToken cancellationToken = default);

    /// <summary>Resumes a persisted slip at its next step or compensation.</summary>
    /// <param name="executor">The executor that owns persistence and policy.</param>
    /// <param name="record">The persisted row.</param>
    /// <param name="cancellationToken">Cancels the execution.</param>
    Task<RoutingSlipResult> ResumeAsync(
        RoutingSlipExecutor executor,
        RoutingSlipRecord record,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Strongly typed routing-slip definition. No reflection is used to select steps: the builder
/// closes the activity types in the generic definition.
/// </summary>
/// <typeparam name="TArguments">The strongly typed slip arguments.</typeparam>
public sealed class RoutingSlipDefinition<TArguments> : IRoutingSlipDefinition where TArguments : class {
    private readonly RoutingSlipStep<TArguments>[] _steps;
    private readonly RoutingSlipStepInfo[] _stepInfos;

    internal RoutingSlipDefinition(string name, RoutingSlipStep<TArguments>[] steps) {
        Name = name;
        _steps = steps;
        _stepInfos = steps.Select(static step => step.Info).ToArray();
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public Type ArgumentsType => typeof(TArguments);

    /// <inheritdoc />
    public IReadOnlyList<RoutingSlipStepInfo> Steps => _stepInfos;

    internal IReadOnlyList<RoutingSlipStep<TArguments>> TypedSteps => _steps;

    Task<RoutingSlipResult> IRoutingSlipDefinition.ExecuteAsync(
        RoutingSlipExecutor executor,
        object arguments,
        string? slipId,
        CancellationToken cancellationToken) {
        Guard.NotNull(executor);
        Guard.NotNull(arguments);

        if(arguments is not TArguments typed) {
            throw new ArgumentException(
                $"Routing-slip definition '{Name}' expects arguments of type '{typeof(TArguments).FullName}', not '{arguments.GetType().FullName}'.",
                nameof(arguments));
        }

        return executor.RunAsync(this, typed, slipId, existing: null, cancellationToken);
    }

    Task<RoutingSlipResult> IRoutingSlipDefinition.ResumeAsync(
        RoutingSlipExecutor executor,
        RoutingSlipRecord record,
        CancellationToken cancellationToken) {
        Guard.NotNull(executor);
        Guard.NotNull(record);

        var arguments = executor.Deserialize<TArguments>(record);

        return executor.RunAsync(this, arguments, record.SlipId, record, cancellationToken);
    }
}
