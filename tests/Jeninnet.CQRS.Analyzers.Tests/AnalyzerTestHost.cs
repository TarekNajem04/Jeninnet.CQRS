//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Analyzers.Tests;

/// <summary>
/// Builds in-memory compilations and runs <see cref="CqrsMisuseAnalyzer"/> over them.
/// Sources that do not compile are rejected loudly: analyzer results on broken code
/// would be meaningless.
/// </summary>
internal static class AnalyzerTestHost {
    internal static async Task<IReadOnlyList<Diagnostic>> GetAnalyzerDiagnosticsAsync(string source) {
        using var workspace = new AdhocWorkspace();

        var references = AppDomain.CurrentDomain.GetAssemblies()
                                                .Where(static assembly => !assembly.IsDynamic && !string.IsNullOrEmpty(assembly.Location))
                                                .Select(static assembly => (MetadataReference)MetadataReference.CreateFromFile(assembly.Location))
                                                .ToList();

        // The message contracts and the handler contracts live in separate assemblies after CQRS-65.
        references.Add(MetadataReference.CreateFromFile(typeof(Jeninnet.CQRS.Interfaces.ICommand<>).Assembly.Location));
        references.Add(MetadataReference.CreateFromFile(typeof(Jeninnet.CQRS.Interfaces.ICommandHandler<,>).Assembly.Location));

        var project = workspace.AddProject("TestProject", LanguageNames.CSharp)
                               .WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest))
                               .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary))
                               .AddMetadataReferences(references)
                               .AddDocument("Test.cs", SourceText.From(source))
                               .Project;

        var compilation = await project.GetCompilationAsync().ConfigureAwait(false)
                          ?? throw new InvalidOperationException("No compilation was produced.");

        var errors = compilation.GetDiagnostics()
                                .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                                .ToList();

        if(errors.Count > 0) {
            throw new InvalidOperationException("Test source has errors: " + string.Join("; ", errors.Select(static error => error.GetMessage())));
        }

        var analyzer = new CqrsMisuseAnalyzer();
        var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(analyzer))
                                           .GetAnalyzerDiagnosticsAsync()
                                           .ConfigureAwait(false);

        return diagnostics.Where(static diagnostic => diagnostic.Id.StartsWith("CQRS", StringComparison.Ordinal)).ToList();
    }
}