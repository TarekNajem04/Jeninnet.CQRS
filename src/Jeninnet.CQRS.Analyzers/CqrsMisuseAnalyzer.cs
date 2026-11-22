//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Analyzers;

/// <summary>
/// Reports Jeninnet.CQRS handler misuse at compile time: duplicate handlers for one message
/// (<c>CQRS0001</c>), swallowed cancellation tokens (<c>CQRS0002</c>), and one handler class
/// claiming several messages (<c>CQRS0003</c>).
/// </summary>
/// <remarks>
/// Only fully closed handler contracts are considered, so open-generic handler bases never
/// produce diagnostics. All checks are syntactic or symbol-based; nothing executes.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CqrsMisuseAnalyzer : DiagnosticAnalyzer {
    private static readonly string[] _handlerNames = ["ICommandHandler", "IQueryHandler", "INotificationHandler"];
    private const string CONTRACTS_NAMESPACE = "Jeninnet.CQRS.Interfaces";
    private const string CANCELLATION_TOKEN_NAME = "System.Threading.CancellationToken";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(
            DiagnosticDescriptors.DuplicateHandler,
            DiagnosticDescriptors.UnusedCancellationToken,
            DiagnosticDescriptors.MultipleMessages);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context) {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start => {
            var registrations = new List<HandlerRegistration>();
            var gate = new object();

            start.RegisterSymbolAction(
                symbol => AnalyzeNamedType(symbol, registrations, gate),
                SymbolKind.NamedType);

            start.RegisterCompilationEndAction(end => ReportDuplicates(end, registrations, gate));
        });
    }

    private static void AnalyzeNamedType(
        SymbolAnalysisContext context,
        List<HandlerRegistration> registrations,
        object gate) {
        if(context.Symbol is not INamedTypeSymbol type ||
           type.TypeKind is not TypeKind.Class ||
           type.IsAbstract) {
            return;
        }

        var contracts = GetClosedHandlerContracts(type);

        if(contracts.Count is 0) {
            return;
        }

        // CQRS0003: one class claiming several messages.
        var distinctMessages = new HashSet<string>(StringComparer.Ordinal);
        foreach(var contract in contracts) {
            distinctMessages.Add(contract.MessageName);
        }

        if(distinctMessages.Count > 1) {
            var ordered = new List<string>(distinctMessages);
            ordered.Sort(StringComparer.Ordinal);
            context.ReportDiagnostic(Diagnostic.Create(
                DiagnosticDescriptors.MultipleMessages,
                IdentifierLocation(type),
                type.Name,
                ordered[0],
                ordered[1]));
        }

        // CQRS0002: HandleAsync implementations ignoring their token.
        foreach(var method in type.GetMembers("HandleAsync").OfType<IMethodSymbol>()) {
            CheckCancellationToken(context, method);
        }

        lock(gate) {
            foreach(var contract in contracts) {
                registrations.Add(new HandlerRegistration(type, contract.MessageName));
            }
        }
    }

    private static void CheckCancellationToken(SymbolAnalysisContext context, IMethodSymbol method) { // NOSONAR: Complexity is inherent to this state machine/pipeline and is covered by tests.
        if(method.IsAbstract) {
            return;
        }

        IParameterSymbol? token = null;
        foreach(var parameter in method.Parameters) { // NOSONAR: Explicit loop is clearer and faster than a LINQ pipeline here.
            if(parameter.Type.ToDisplayString() == CANCELLATION_TOKEN_NAME) {
                token = parameter;
                break;
            }
        }

        if(token is null || token.Name == "_") {
            return;
        }

        foreach(var reference in method.DeclaringSyntaxReferences) {
            if(reference.GetSyntax(context.CancellationToken) is not MethodDeclarationSyntax declaration) {
                continue;
            }

            var usesToken = false;
            foreach(var identifier in declaration.DescendantNodes().OfType<IdentifierNameSyntax>()) {
                if(identifier.Identifier.ValueText == token.Name) {
                    usesToken = true;
                    break;
                }
            }

            if(!usesToken) {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.UnusedCancellationToken,
                    token.Locations.Length > 0 ? token.Locations[0] : declaration.Identifier.GetLocation(),
                    token.Name));
                return;
            }

            // Checked one declaration; partial parts share the name, keep it simple and stop.
            return;
        }
    }

    private static void ReportDuplicates(
        CompilationAnalysisContext context,
        List<HandlerRegistration> registrations,
        object gate) {
        var groups = new Dictionary<string, List<INamedTypeSymbol>>(StringComparer.Ordinal);

        lock(gate) {
            foreach(var registration in registrations) {
                if(!groups.TryGetValue(registration.MessageName, out var handlers)) {
                    handlers = [];
                    groups[registration.MessageName] = handlers;
                }

                var already = false;
                foreach(var existing in handlers) { // NOSONAR: Explicit loop is clearer and faster than a LINQ pipeline here.
                    if(SymbolEqualityComparer.Default.Equals(existing, registration.Handler)) {
                        already = true;
                        break;
                    }
                }

                if(!already) {
                    handlers.Add(registration.Handler);
                }
            }
        }

        foreach(var pair in groups) {
            if(pair.Value.Count < 2) {
                continue;
            }

            var ordered = new List<INamedTypeSymbol>(pair.Value);
            ordered.Sort(static (left, right) => string.CompareOrdinal(
                left.ToDisplayString(),
                right.ToDisplayString()));

            for(var i = 1; i < ordered.Count; i++) {
                context.ReportDiagnostic(Diagnostic.Create(
                    DiagnosticDescriptors.DuplicateHandler,
                    IdentifierLocation(ordered[i]),
                    pair.Key,
                    ordered[0].Name,
                    ordered[i].Name));
            }
        }
    }

    private static List<HandlerContract> GetClosedHandlerContracts(INamedTypeSymbol type) {
        var contracts = new List<HandlerContract>();

        foreach(var contract in type.AllInterfaces) {
            if(contract is not INamedTypeSymbol named ||
               !IsHandlerContractName(named.Name, named.Arity)) {
                continue;
            }

            if(named.ContainingNamespace?.ToDisplayString() != CONTRACTS_NAMESPACE) {
                continue;
            }

            // Open generics (e.g. Foo<T> : ICommandHandler<T, R>) are deployment decisions,
            // not concrete duplicates: only fully-closed contracts participate.
            var open = false;
            foreach(var argument in named.TypeArguments) { // NOSONAR: Explicit loop is clearer and faster than a LINQ pipeline here.
                if(argument is ITypeParameterSymbol) {
                    open = true;
                    break;
                }
            }

            if(open || named.TypeArguments.Length is 0) {
                continue;
            }

            contracts.Add(new HandlerContract(named.TypeArguments[0].ToDisplayString()));
        }

        return contracts;
    }

    private static bool IsHandlerContractName(string name, int arity) {
        foreach(var handler in _handlerNames) { // NOSONAR: Explicit loop is clearer and faster than a LINQ pipeline here.
            if(name == handler) {
                return arity is 1 or 2;
            }
        }

        return false;
    }

    private static Location IdentifierLocation(INamedTypeSymbol type) {
        foreach(var reference in type.DeclaringSyntaxReferences) {
            if(reference.GetSyntax() is ClassDeclarationSyntax declaration) {
                return declaration.Identifier.GetLocation();
            }

            if(reference.GetSyntax() is RecordDeclarationSyntax record) {
                return record.Identifier.GetLocation();
            }
        }

        return type.Locations.Length > 0 ? type.Locations[0] : Location.None;
    }

    private sealed class HandlerRegistration(INamedTypeSymbol Handler, string MessageName) {
        public INamedTypeSymbol Handler { get; } = Handler;
        public string MessageName { get; } = MessageName;
    }

    private sealed class HandlerContract(string MessageName) {
        public string MessageName { get; } = MessageName;
    }
}