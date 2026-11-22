//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RoutingSlips;

/// <summary>
/// Fluent definition builder. Steps are strongly typed activities; the builder captures their
/// order and per-step policy, then freezes a <see cref="RoutingSlipDefinition{TArguments}"/>.
/// </summary>
/// <typeparam name="TArguments">The strongly typed slip arguments.</typeparam>
public sealed class RoutingSlipBuilder<TArguments> where TArguments : class {
    private readonly string _name;
    private readonly List<RoutingSlipStep<TArguments>> _steps = [];

    /// <summary>Initializes a builder with a stable definition name.</summary>
    /// <param name="name">The definition name stored with every slip.</param>
    public RoutingSlipBuilder(string name) {
        if(StringCompat.IsNullOrWhiteSpace(name)) {
            throw new ArgumentException("A routing-slip definition needs a non-empty name.", nameof(name));
        }

        _name = name;
    }

    /// <summary>Adds one activity step with an explicit step id.</summary>
    /// <typeparam name="TActivity">The activity implementation.</typeparam>
    /// <param name="name">The stable step id.</param>
    /// <returns>This builder.</returns>
    public RoutingSlipBuilder<TArguments> AddStep<TActivity>(string name)
        where TActivity : class, IActivity<TArguments> =>
        AddStep<TActivity>(configure: null, name);

    /// <summary>Adds one activity step.</summary>
    /// <typeparam name="TActivity">The activity implementation.</typeparam>
    /// <param name="configure">Optional execution and compensation policy.</param>
    /// <param name="name">A stable step id; defaults to the activity type name.</param>
    /// <returns>This builder.</returns>
    public RoutingSlipBuilder<TArguments> AddStep<TActivity>(
        Action<RoutingSlipStepOptions>? configure = null,
        string? name = null)
        where TActivity : class, IActivity<TArguments> {
        var stepName = StringCompat.IsNullOrWhiteSpace(name) ? typeof(TActivity).Name : name;

        if(_steps.Any(step => string.Equals(step.Name, stepName, StringComparison.Ordinal))) {
            throw new InvalidOperationException(
                $"Routing-slip definition '{_name}' already contains a step named '{stepName}'.");
        }

        var options = new RoutingSlipStepOptions();
        configure?.Invoke(options);
        options.Validate(stepName);
        _steps.Add(new RoutingSlipStep<TArguments>(stepName, typeof(TActivity), options.Copy()));

        return this;
    }

    /// <summary>Freezes the definition.</summary>
    /// <returns>The immutable definition.</returns>
    /// <exception cref="InvalidOperationException">The slip has no steps.</exception>
    public RoutingSlipDefinition<TArguments> Build() {
        if(_steps.Count is 0) {
            throw new InvalidOperationException($"Routing-slip definition '{_name}' has no steps.");
        }

        return new RoutingSlipDefinition<TArguments>(_name, [.. _steps]);
    }
}