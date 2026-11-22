//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Jeninnet.CQRS.Generators;

/// <summary>
/// Source generator that automatically discovers and registers CQRS handlers.
/// </summary>
public class CqrsSourceGenerator : IIncrementalGenerator {
    /// <summary>Registers the generation pipeline.</summary>
    /// <param name="context">The incremental generator context.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context) {
        // Phase 1: Discover potential handler classes (Syntax-based)
        var classDeclarations = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: (node, _) => node is ClassDeclarationSyntax classDecl && classDecl.BaseList != null,
                transform: (ctx, _) => {
                    var classDecl = (ClassDeclarationSyntax)ctx.Node;
                    return ctx.SemanticModel?.GetDeclaredSymbol(classDecl, cancellationToken: _);
                }
            )
            .Where(static x => x is not null);

        // Phase 2: Filter for those implementing CQRS interfaces (Semantic-based)
        var handlers = classDeclarations.Select((x, _) => {
            var symbol = x!;
            var handlerInterface = symbol.AllInterfaces.FirstOrDefault(i => i.Name is "ICommandHandler" or "IQueryHandler" or "IStreamQueryHandler" or "INotificationHandler");

            if(handlerInterface == null) {
                return null;
            }

            return new HandlerDiscoveryModel(
                HandlerType: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                InterfaceType: handlerInterface.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                AssemblyName: symbol.ContainingAssembly?.Name ?? "UnknownAssembly",
                SourceLocation: symbol.Locations[0].ToString()
            );
        }).Where(static x => x is not null);

        // Phase 3: Combine and emit
        context.RegisterSourceOutput(handlers.Collect(), (pipeline, source) => {
            var handlersList = source.OfType<HandlerDiscoveryModel>().ToList();
            if(handlersList.Count == 0) {
                return;
            }

            // Duplicate Detection (T011)
            var duplicates = handlersList
                .GroupBy(h => h.InterfaceType)
                .Where(g => g.Count() > 1)
                .ToList();

            if(duplicates.Count != 0) {
                // In a real generator, we would use the context.ReportDiagnostic approach.
                // For the MVP, we add a warning to the generated code to alert the developer.
                GenerateDuplicateWarning(pipeline, duplicates);
            }

            var assemblyName = handlersList[0].AssemblyName;
            var sourceCode = GenerateRegistrationCode(assemblyName, handlersList);

            pipeline.AddSource($"CqrsRegistrationExtensions_{assemblyName}.g.cs", SourceText.From(sourceCode, Encoding.UTF8));
        });
    }

    private static void GenerateDuplicateWarning(SourceProductionContext pipeline, List<IGrouping<string, HandlerDiscoveryModel>> duplicates) {
        var sb = new StringBuilder();
        sb.AppendLine("// WARNING: Duplicate CQRS Handlers detected!");
        foreach(var group in duplicates) {
            sb.AppendLine($"// Interface {group.Key} is implemented by multiple handlers: {string.Join(", ", group.Select(h => h.HandlerType))}");
        }

        sb.AppendLine("// This may cause unpredictable behavior in the DI container.");

        pipeline.AddSource("CqrsRegistrationWarnings.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static string GenerateRegistrationCode(string assemblyName, List<HandlerDiscoveryModel> handlers) {
        var sb = new StringBuilder();
        sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
        sb.AppendLine();
        sb.AppendLine("namespace Jeninnet.CQRS.Extensions;");
        sb.AppendLine();
        sb.AppendLine("public static partial class CqrsRegistrationExtensions {");
        sb.AppendLine($"    public static IServiceCollection AddCqrsHandlersFrom{assemblyName}(this IServiceCollection services) {{");

        foreach(var handler in handlers) {
            sb.AppendLine($"        services.AddTransient<{handler.InterfaceType}, {handler.HandlerType}>();");
        }

        sb.AppendLine("        return services;");
        sb.AppendLine("    }");
        sb.AppendLine("}");
        return sb.ToString();
    }
}
