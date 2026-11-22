//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
#pragma warning disable RS1038 // Compiler extensions should be implemented in assemblies with compiler-provided references
namespace Jeninnet.CQRS.Generators;

/// <summary>
/// Source generator that emits convention-based handler registrations.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class CqrsRegistrationGenerator : IIncrementalGenerator {
    private const string CommandInterfaceName = "ICommand";

    /// <summary>Registers the generation pipeline.</summary>
    /// <param name="context">The incremental generator context.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        // Every class can be a convention handler, so the predicate no longer requires a base list.
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => s is ClassDeclarationSyntax,
                transform: static (ctx, _) => GetSemanticTargetForGeneration(ctx))
            .Where(static m => m is not null);

        // Commands can be records, structs, or classes. Scan every type declaration so the
        // generated deferred-envelope registry covers both ICommand and ICommand<T> shapes.
        var typeDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (s, _) => s is TypeDeclarationSyntax { BaseList: not null },
                transform: static (ctx, _) => GetNamedTypeForGeneration(ctx))
            .Where(static m => m is not null);

        var compilationAndClasses = context.CompilationProvider
            .Combine(classDeclarations.Collect())
            .Combine(typeDeclarations.Collect());

        context.RegisterSourceOutput(compilationAndClasses, static (spc, source) =>
            Execute(source.Left.Item1, source.Left.Item2!, source.Right!, spc));
    }

    private static ClassDeclarationSyntax? GetSemanticTargetForGeneration(GeneratorSyntaxContext context) =>
        // We just return the class; the heavy lifting happens in Execute
        (ClassDeclarationSyntax)context.Node;

    private static INamedTypeSymbol? GetNamedTypeForGeneration(GeneratorSyntaxContext context) =>
        context.SemanticModel.GetDeclaredSymbol((TypeDeclarationSyntax)context.Node);

    private static void Execute( // NOSONAR: Complexity is inherent to this state machine/pipeline and is covered by tests.
        Compilation compilation,
        ImmutableArray<ClassDeclarationSyntax> classes,
        ImmutableArray<INamedTypeSymbol?> typeDeclarations,
        SourceProductionContext context) {
        var registrations = new List<string>();
        var adapters = new List<string>();
        var conventionRegistrations = new List<string>();
        var interfacesToMatch = new[] { "ICommandHandler", "IQueryHandler", "IStreamQueryHandler", "INotificationHandler", "IPipelineBehavior", "IPreProcessor", "IPostProcessor" };
        var explicitMessages = new HashSet<string>(StringComparer.Ordinal);
        var symbols = new List<INamedTypeSymbol>();

        foreach(var classDecl in classes.Distinct()) {
            var model = compilation.GetSemanticModel(classDecl.SyntaxTree);
            if(model.GetDeclaredSymbol(classDecl, context.CancellationToken) is INamedTypeSymbol typeSymbol) {
                symbols.Add(typeSymbol);
            }
        }

        // Pass 1: explicit interface handlers (they always win over convention binding).
        foreach(var typeSymbol in symbols) {
            if(typeSymbol.IsAbstract) {
                continue;
            }

            foreach(var iface in typeSymbol.AllInterfaces.Where(iface => interfacesToMatch.Any(name => iface.Name == name))) {
                var serviceType = iface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var implType = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                registrations.Add($"            services.AddTransient(typeof({serviceType}), typeof({implType}));");

                if(iface.Name is "ICommandHandler" or "IQueryHandler" or "INotificationHandler" && iface.TypeArguments.Length > 0) {
                    explicitMessages.Add(iface.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
                }
            }
        }

        // Pass 2: convention handlers (Handle/HandleAsync) without a handler interface.
        var index = 0;

        foreach(var typeSymbol in symbols) {
            if(typeSymbol.IsAbstract || typeSymbol.IsStatic || typeSymbol.TypeParameters.Length > 0 || typeSymbol.ContainingType is not null || !IsAccessible(typeSymbol)) {
                continue;
            }

            if(typeSymbol.AllInterfaces.Any(iface => interfacesToMatch.Any(name => iface.Name == name))) {
                continue;
            }

            foreach(var method in typeSymbol.GetMembers("HandleAsync").OfType<IMethodSymbol>().Concat(typeSymbol.GetMembers("Handle").OfType<IMethodSymbol>())) {
                if(!TryDescribeConvention(method, out var descriptor) || explicitMessages.Contains(descriptor.MessageType)) {
                    continue;
                }

                var adapterName = $"__CqrsConventionAdapter_{index++}";
                adapters.Add(BuildAdapter(adapterName, typeSymbol, method, descriptor));
                conventionRegistrations.Add($"            services.AddTransient(typeof({typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}), typeof({typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}));");
                conventionRegistrations.Add($"            services.AddTransient(typeof({descriptor.ServiceType}), typeof({adapterName}));");

                // A second convention method for the same message is ignored (explicit, deterministic).
                explicitMessages.Add(descriptor.MessageType);

                break;
            }
        }

        var allRegistrations = registrations.Concat(conventionRegistrations).Distinct().ToList();
        var registryCases = new List<(string CommandType, string? ResultType)>();
        var seenCommands = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);

        foreach(var typeSymbol in typeDeclarations) {
            if(typeSymbol is null || !seenCommands.Add(typeSymbol) || !TryDescribeCommand(typeSymbol, out var resultType)) {
                continue;
            }

            registryCases.Add((typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), resultType));
        }

        var registry = BuildRegistry(registryCases);

        var code =
        $$"""
        // <auto-generated/>
        #nullable enable
        #pragma warning disable CS8600, CS8601, CS8602, CS8603, CS8604 // Disable common nullable warnings in generated code
        using Microsoft.Extensions.DependencyInjection;
        using Microsoft.Extensions.DependencyInjection.Extensions;
        using Microsoft.Extensions.Configuration;
        using System.Reflection;
        using Jeninnet.CQRS;
        using Jeninnet.CQRS.Interfaces;
        using Jeninnet.CQRS.Configurations;
        using Jeninnet.CQRS.Extensions; // To ensure visibility of internal types if necessary

        namespace Jeninnet.CQRS.Extensions;

        public static class ServiceCollectionExtensions {
            /// <summary>
            /// Core registration logic generated at compile-time.
            /// </summary>
            public static IServiceCollection AddCqrs(this IServiceCollection services) {
                ArgumentNullException.ThrowIfNull(services);

                // 1. Register the mediator and dispatchers (The Plumbing)
                services.AddSingleton<ICqrsBroker, CqrsBroker>();
                services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
                services.AddSingleton<IQueryDispatcher, QueryDispatcher>();

                // 2. Register Discovered Handlers (The Discovery - Generated)
                // This replaces the runtime Assembly scanning logic
                {{string.Join("\n        ", allRegistrations)}}

                // 3. Register the generated deferred-envelope registry (CQRS-39). Durable
                // rehydration uses this closed registry first; stores keep an annotated
                // reflection fallback for commands declared outside this compilation.
                services.TryAddSingleton<global::Jeninnet.CQRS.DeferredDelivery.IDeferredEnvelopeFactory, DeferredEnvelopeRegistry>();

                return services;
            }
        }

        {{string.Join("\n\n", adapters)}}

        {{registry}}
        """;
        context.AddSource("ServiceCollectionExtensions.g.cs", SourceText.From(code, Encoding.UTF8));
    }

    private static bool TryDescribeCommand(INamedTypeSymbol type, out string? resultType) {
        resultType = null;

        if(type.IsAbstract || type.IsStatic || type.IsRefLikeType || type.TypeKind is not (TypeKind.Class or TypeKind.Struct) || !IsRegistryAccessible(type)) {
            return false;
        }

        var interfaces = type.AllInterfaces;
        var hasVoidCommand = interfaces.Any(static iface =>
            iface.Name == CommandInterfaceName &&
            iface.Arity is 0 &&
            iface.ContainingNamespace?.ToDisplayString() == "Jeninnet.CQRS.Interfaces");

        if(hasVoidCommand) {
            return true;
        }

        var resultInterface = interfaces.FirstOrDefault(static iface =>
            iface.Name == CommandInterfaceName &&
            iface.Arity is 1 &&
            iface.ContainingNamespace?.ToDisplayString() == "Jeninnet.CQRS.Interfaces");

        if(resultInterface is null) {
            return false;
        }

        resultType = resultInterface.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return true;
    }

    private static bool IsRegistryAccessible(INamedTypeSymbol type) {
        for(var current = type; current is not null; current = current.ContainingType) {
            if(current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal) || current.TypeParameters.Length > 0) {
                return false;
            }
        }

        return true;
    }

    private static string BuildRegistry(IReadOnlyList<(string CommandType, string? ResultType)> commands) {
        var builder = new StringBuilder();
        builder.AppendLine("    /// <summary>");
        builder.AppendLine("    /// Generated registry of every command in this compilation: durable deferred delivery");
        builder.AppendLine("    /// rehydrates these without reflection. Commands declared in other assemblies fall back");
        builder.AppendLine("    /// to the annotated reflection path in the store.");
        builder.AppendLine("    /// </summary>");
        builder.AppendLine("    internal sealed class DeferredEnvelopeRegistry : global::Jeninnet.CQRS.DeferredDelivery.IDeferredEnvelopeFactory {");
        builder.AppendLine("        private readonly global::Jeninnet.CQRS.Serialization.IMessageSerializer _serializer;");
        builder.AppendLine();
        builder.AppendLine("        /// <summary>Initializes the registry.</summary>");
        builder.AppendLine("        /// <param name=\"serializer\">The payload serializer. Never null.</param>");
        builder.AppendLine("        public DeferredEnvelopeRegistry(global::Jeninnet.CQRS.Serialization.IMessageSerializer serializer) =>");
        builder.AppendLine("            _serializer = serializer ?? throw new global::System.ArgumentNullException(nameof(serializer));");
        builder.AppendLine();
        builder.AppendLine("        /// <inheritdoc />");
        builder.AppendLine("        public bool TryCreate(");
        builder.AppendLine("            string commandTypeName,");
        builder.AppendLine("            global::System.ReadOnlySpan<byte> payload,");
        builder.AppendLine("            global::System.DateTimeOffset dueUtc,");
        builder.AppendLine("            global::System.DateTimeOffset? expiresAtUtc,");
        builder.AppendLine("            int version,");
        builder.AppendLine("            global::Jeninnet.CQRS.DeferredDelivery.RecurringEnvelopeMetadata? recurring,");
        builder.AppendLine("            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out global::Jeninnet.CQRS.DeferredDelivery.IDeferredEnvelope? envelope) {");

        foreach(var (commandType, resultType) in commands) {
            AppendCreateCase(builder, commandType, resultType);
        }

        builder.AppendLine("            envelope = null;");
        builder.AppendLine();
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        /// <inheritdoc />");
        builder.AppendLine("        public bool TryWrap(");
        builder.AppendLine("            object command,");
        builder.AppendLine("            global::System.DateTimeOffset dueUtc,");
        builder.AppendLine("            global::System.DateTimeOffset? expiresAtUtc,");
        builder.AppendLine("            int version,");
        builder.AppendLine("            global::Jeninnet.CQRS.DeferredDelivery.RecurringEnvelopeMetadata? recurring,");
        builder.AppendLine("            [global::System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out global::Jeninnet.CQRS.DeferredDelivery.IDeferredEnvelope? envelope) {");

        var wrapIndex = 0;

        foreach(var (commandType, resultType) in commands) {
            AppendWrapCase(builder, commandType, resultType, wrapIndex++);
        }

        builder.AppendLine("            envelope = null;");
        builder.AppendLine();
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static bool IsType(string commandTypeName, global::System.Type type) =>");
        builder.AppendLine("            global::System.String.Equals(commandTypeName, type.AssemblyQualifiedName, global::System.StringComparison.Ordinal)");
        builder.AppendLine("            || global::System.String.Equals(commandTypeName, type.FullName, global::System.StringComparison.Ordinal);");
        builder.AppendLine("    }");

        return builder.ToString();
    }

    private static void AppendCreateCase(StringBuilder builder, string commandType, string? resultType) {
        builder.Append("            if(IsType(commandTypeName, typeof(").Append(commandType).AppendLine("))) {");
        builder.Append("                var command = (").Append(commandType).Append("?)_serializer.Deserialize(payload, typeof(").Append(commandType).AppendLine("));");
        builder.AppendLine();
        builder.AppendLine("                if(command is null) {");
        builder.AppendLine("                    envelope = null;");
        builder.AppendLine();
        builder.AppendLine("                    return false;");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.Append("                envelope = ").Append(BuildEnvelopeExpression(commandType, resultType, "command")).AppendLine(";");
        builder.AppendLine();
        builder.AppendLine("                return true;");
        builder.AppendLine("            }");
        builder.AppendLine();
    }

    private static void AppendWrapCase(StringBuilder builder, string commandType, string? resultType, int index) {
        var variable = $"typed{index}";

        builder.Append("            if(command.GetType() == typeof(").Append(commandType).Append(") && command is ").Append(commandType).Append(' ').Append(variable).AppendLine(") {");
        builder.Append("                envelope = ").Append(BuildEnvelopeExpression(commandType, resultType, variable)).AppendLine(";");
        builder.AppendLine();
        builder.AppendLine("                return true;");
        builder.AppendLine("            }");
        builder.AppendLine();
    }

    private static string BuildEnvelopeExpression(string commandType, string? resultType, string variableName) {
        if(resultType is not null) {
            return $"recurring is null\n                    ? new global::Jeninnet.CQRS.DeferredDelivery.DeferredResultEnvelope<{commandType}, {resultType}>({variableName}, dueUtc, expiresAtUtc, version)\n                    : throw new global::System.InvalidOperationException(\"Recurring delivery requires an ICommand (void) command type.\")";
        }

        var oneShot = $"new global::Jeninnet.CQRS.DeferredDelivery.DeferredEnvelope<{commandType}>({variableName}, dueUtc, expiresAtUtc, version)";
        var recurring = $"new global::Jeninnet.CQRS.Recurring.RecurringEnvelope<{commandType}>({variableName}, global::Jeninnet.CQRS.Recurring.CronExpression.Parse(recurring.CronExpression), recurring.UniqueKey, global::System.TimeZoneInfo.FindSystemTimeZoneById(recurring.TimeZoneId), recurring.Overlap, dueUtc, expiresAtUtc, version)";

        return $"recurring is null\n                    ? {oneShot}\n                    : {recurring}";
    }

    private static string BuildAdapter(string adapterName, INamedTypeSymbol handlerType, IMethodSymbol method, ConventionDescriptor descriptor) {
        var implType = handlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var invocation = method.Parameters.Length is 2
            ? $"_inner.{method.Name}(command, cancellationToken)"
            : $"_inner.{method.Name}(command)";

        var returnClause = descriptor.Kind switch {
            ConventionKind.Void => $$"""
                    public async global::System.Threading.Tasks.Task<global::Jeninnet.CQRS.Unit> HandleAsync({{descriptor.MessageType}} command, global::System.Threading.CancellationToken cancellationToken) {
                        await {{invocation}}.ConfigureAwait(false);
                        return global::Jeninnet.CQRS.Unit.Value;
                    }
        """,
            _ => $$"""
                    public global::System.Threading.Tasks.Task<{{descriptor.ResultType}}> HandleAsync({{descriptor.MessageType}} command, global::System.Threading.CancellationToken cancellationToken) =>
                        {{invocation}};
        """,
        };

        var notificationClause = $$"""
                    public global::System.Threading.Tasks.Task HandleAsync({{descriptor.MessageType}} command, global::System.Threading.CancellationToken cancellationToken) =>
                        {{invocation}};
        """;

        var body = descriptor.Kind is ConventionKind.Notification ? notificationClause : returnClause;

        return $$"""
            /// <summary>Generated adapter for the convention handler <see cref="{{implType.Replace("<", "{").Replace(">", "}")}}"/>.</summary>
            public sealed class {{adapterName}} : {{descriptor.ServiceType}} {
                private readonly {{implType}} _inner;

                public {{adapterName}}({{implType}} inner) => _inner = inner ?? throw new global::System.ArgumentNullException(nameof(inner));

        {{body}}
            }
        """;
    }

    private static bool TryDescribeConvention(IMethodSymbol method, out ConventionDescriptor descriptor) { // NOSONAR: Complexity is inherent to this state machine/pipeline and is covered by tests.
        descriptor = default;

        if(method.IsStatic || method.IsAbstract || method.IsGenericMethod || method.DeclaredAccessibility is not Accessibility.Public) {
            return false;
        }

        var returnType = method.ReturnType as INamedTypeSymbol;

        if(returnType is null || returnType.Name != "Task" || returnType.ContainingNamespace?.ToDisplayString() != "System.Threading.Tasks") {
            return false;
        }

        if(method.Parameters.Length is not (1 or 2)) {
            return false;
        }

        if(method.Parameters.Length is 2 && method.Parameters[1].Type.ToDisplayString() != "System.Threading.CancellationToken") {
            return false;
        }

        if(method.Parameters[0].Type is not INamedTypeSymbol message) {
            return false;
        }

        var messageType = message.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        if(message.AllInterfaces.Any(static i => i.ToDisplayString() == "Jeninnet.CQRS.Interfaces.INotification")) {
            descriptor = new ConventionDescriptor($"global::Jeninnet.CQRS.Interfaces.INotificationHandler<{messageType}>", messageType, null, ConventionKind.Notification);

            return true;
        }

        var isVoidCommand = message.AllInterfaces.Any(static i => i.Name == CommandInterfaceName && i.Arity == 0);
        var isResultCommand = message.AllInterfaces.Any(static i => i.Name == CommandInterfaceName && i.Arity == 1);
        var isQuery = message.AllInterfaces.Any(static i => i.Name == "IQuery" && i.Arity == 1);

        if(!isVoidCommand && !isResultCommand && !isQuery) {
            return false;
        }

        if(isVoidCommand) {
            if(returnType.TypeArguments.Length is 0) {
                descriptor = new ConventionDescriptor($"global::Jeninnet.CQRS.Interfaces.ICommandHandler<{messageType}>", messageType, "global::Jeninnet.CQRS.Unit", ConventionKind.Void);

                return true;
            }

            // Task<Unit> is the interface shape already; wrap nothing.
            if(returnType.TypeArguments.Length is 1 && returnType.TypeArguments[0].ToDisplayString() == "Jeninnet.CQRS.Unit") {
                descriptor = new ConventionDescriptor($"global::Jeninnet.CQRS.Interfaces.ICommandHandler<{messageType}>", messageType, "global::Jeninnet.CQRS.Unit", ConventionKind.Void);

                return true;
            }

            return false;
        }

        if(returnType.TypeArguments.Length is not 1) {
            return false;
        }

        var resultType = returnType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        descriptor = isQuery
            ? new ConventionDescriptor($"global::Jeninnet.CQRS.Interfaces.IQueryHandler<{messageType}, {resultType}>", messageType, resultType, ConventionKind.Query)
            : new ConventionDescriptor($"global::Jeninnet.CQRS.Interfaces.ICommandHandler<{messageType}, {resultType}>", messageType, resultType, ConventionKind.Command);

        return true;
    }

    private static bool IsAccessible(INamedTypeSymbol type) =>
        type.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal;

    private enum ConventionKind {
        Void,
        Command,
        Query,
        Notification,
    }

    private readonly record struct ConventionDescriptor(string ServiceType, string MessageType, string? ResultType, ConventionKind Kind);
}