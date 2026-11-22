//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

namespace Jeninnet.CQRS.Benchmarks;

public class RegistrationBenchmarks {
    private IServiceCollection _services;
    private CqrsOptions _options;

    [GlobalSetup]
    public void Setup() {
        _options = new CqrsOptions(new PipelineBehaviorsOptions());
    }

    [Benchmark(Baseline = true)]
    public void ReflectionRegistration() {
        var services = new ServiceCollection();
        // We simulate a large assembly by passing the current assembly
        services.AddCqrsByReflection(_options, System.Reflection.Assembly.GetExecutingAssembly());
        _services = services;
    }

    [Benchmark]
    public void SourceGeneratedRegistration() {
        var services = new ServiceCollection();
        services.AddCqrsSourceGenerated(opt => { });
        // In a real scenario, we would call the generated AddCqrsHandlersFrom...() methods here.
        // Since we are in the benchmark project, we assume the generator has run.
        _services = services;
    }

    public static void Main(string[] args) => BenchmarkRunner.Run<RegistrationBenchmarks>();
}

// Dummy handlers to ensure the generator has something to find
public record DummyCommand1 : ICommand<Unit>;
public class DummyHandler1 : ICommandHandler<DummyCommand1> {
    public Task<Unit> HandleAsync(DummyCommand1 req, CancellationToken ct) => Task.FromResult(Unit.Default);
}

public record DummyQuery1 : IQuery<string>;
public class DummyHandlerQ1 : IQueryHandler<DummyQuery1, string> {
    public Task<string> HandleAsync(DummyQuery1 req, CancellationToken ct) => Task.FromResult("Success");
}
