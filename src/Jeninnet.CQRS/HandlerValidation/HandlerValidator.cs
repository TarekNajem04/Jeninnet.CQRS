//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

namespace Jeninnet.CQRS.HandlerValidation;

/// <summary>
/// Default validator: resolves every closed handler/behavior registration, checks duplicate
/// single-handler messages, and detects singleton→scoped captive dependencies. Missing handlers
/// for a message are deliberately not errors — a command may legitimately have no handler in a
/// given deployment.
/// </summary>
internal sealed class HandlerValidator(IServiceProvider provider, HandlerRegistrationSnapshot snapshot, IOptions<HandlerValidationOptions> options) : IHandlerValidator {
    private static readonly string[] SingleHandlerContracts = ["ICommandHandler", "IQueryHandler", "IStreamQueryHandler"];
    private static readonly string[] HandlerContracts = ["ICommandHandler", "IQueryHandler", "IStreamQueryHandler", "INotificationHandler", "IPipelineBehavior"];

    private readonly IServiceProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    private readonly HandlerRegistrationSnapshot _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
    private readonly HandlerValidationOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;

    /// <inheritdoc />
    public HandlerValidationReport Validate() {
        var findings = new List<HandlerValidationFinding>();
        var lifetimes = BuildLifetimes();

        foreach(var descriptor in _snapshot.Descriptors) {
            if(!IsHandlerContract(descriptor.ServiceType)) {
                continue;
            }

            if(descriptor.ImplementationType is null) {
                continue; // Factory registrations are opaque to constructor analysis; resolution still runs below.
            }

            CheckCaptive(descriptor, lifetimes, findings);
        }

        CheckDuplicates(findings);
        CheckResolvable(findings);

        findings.Sort(static (left, right) => string.CompareOrdinal(left.ToString(), right.ToString()));

        return new HandlerValidationReport(findings);
    }

    private void CheckCaptive(ServiceDescriptor descriptor, Dictionary<Type, ServiceLifetime> lifetimes, List<HandlerValidationFinding> findings) {
        if(!_options.CheckCaptiveDependencies || descriptor.Lifetime is not ServiceLifetime.Singleton) {
            return;
        }

        var implementation = descriptor.ImplementationType!;
        var constructor = implementation.GetConstructors().OrderByDescending(static c => c.GetParameters().Length).FirstOrDefault();

        if(constructor is null) {
            return;
        }

        foreach(var parameter in constructor.GetParameters()) { // NOSONAR: Explicit loop is clearer and faster than a LINQ pipeline here.
            if(lifetimes.TryGetValue(parameter.ParameterType, out var lifetime) && lifetime is ServiceLifetime.Scoped) {
                findings.Add(new HandlerValidationFinding(
                    HandlerValidationKind.CaptiveDependency,
                    descriptor.ServiceType,
                    implementation,
                    $"Singleton '{implementation.Name}' depends on scoped '{parameter.ParameterType.Name}'. Register the handler as transient/scoped or the dependency as singleton."));
            }
        }
    }

    private void CheckDuplicates(List<HandlerValidationFinding> findings) {
        if(!_options.CheckDuplicateHandlers) {
            return;
        }

        var groups = new Dictionary<Type, List<Type>>();

        foreach(var descriptor in _snapshot.Descriptors) {
            if(!IsSingleHandlerContract(descriptor.ServiceType) || descriptor.ServiceType.ContainsGenericParameters) {
                continue;
            }

            if(!groups.TryGetValue(descriptor.ServiceType, out var implementations)) {
                implementations = [];
                groups[descriptor.ServiceType] = implementations;
            }

            var implementation = descriptor.ImplementationType ?? descriptor.ServiceType;

            if(!implementations.Contains(implementation)) {
                implementations.Add(implementation);
            }
        }

        foreach(var group in groups) {
            if(group.Value.Count < 2) {
                continue;
            }

            findings.Add(new HandlerValidationFinding(
                HandlerValidationKind.DuplicateHandler,
                group.Key,
                null,
                $"'{group.Key.Name}' has {group.Value.Count} handlers ({string.Join(", ", group.Value.Select(static type => type.Name))}); only one will run."));
        }
    }

    private void CheckResolvable(List<HandlerValidationFinding> findings) {
        using var scope = _provider.CreateScope();

        foreach(var descriptor in _snapshot.Descriptors) {
            if(!IsHandlerContract(descriptor.ServiceType) || descriptor.ServiceType.ContainsGenericParameters) {
                continue;
            }

            try {
                _ = scope.ServiceProvider.GetRequiredService(descriptor.ServiceType);
            }
            catch(Exception exception) {
                var innermost = exception;

                while(innermost.InnerException is not null) {
                    innermost = innermost.InnerException;
                }

                findings.Add(new HandlerValidationFinding(
                    HandlerValidationKind.UnresolvableDependency,
                    descriptor.ServiceType,
                    descriptor.ImplementationType,
                    $"Cannot construct '{descriptor.ServiceType.Name}': {innermost.Message}"));
            }
        }
    }

    private Dictionary<Type, ServiceLifetime> BuildLifetimes() {
        var lifetimes = new Dictionary<Type, ServiceLifetime>();

        foreach(var descriptor in _snapshot.Descriptors) {
            if(!descriptor.ServiceType.ContainsGenericParameters) {
                lifetimes[descriptor.ServiceType] = descriptor.Lifetime;
            }
        }

        return lifetimes;
    }

    private static bool IsHandlerContract(Type serviceType) =>
        serviceType.IsGenericType && Unadorned(serviceType) is { } name && HandlerContracts.Contains(name, StringComparer.Ordinal);

    private static bool IsSingleHandlerContract(Type serviceType) =>
        serviceType.IsGenericType && Unadorned(serviceType) is { } name && SingleHandlerContracts.Contains(name, StringComparer.Ordinal);

    // Reflection appends the arity to generic type names ("ICommandHandler`2").
    private static string Unadorned(Type type) {
        var name = type.Name;
        var tick = name.IndexOf('`');

        return tick < 0 ? name : name.Substring(0, tick);
    }
}