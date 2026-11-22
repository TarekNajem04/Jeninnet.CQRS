//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Benchmarks;

/// <summary>
/// Dispatch overhead: a bare handler versus the same handler behind four pass-through
/// pipeline behaviors. The bare case is the class baseline, so the ratio is hardware-independent.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 10)]
public class DispatchBenchmarks {
    private ICommandDispatcher _bare = null!;
    private ICommandDispatcher _pipeline = null!;
    private readonly Ping _command = new("bench");

    [GlobalSetup]
    public void Setup() {
        _bare = Build(static _ => { });
        _pipeline = Build(static services => {
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PassThroughBehavior<,>));
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PassThroughBehavior<,>));
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PassThroughBehavior<,>));
            services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PassThroughBehavior<,>));
        });
    }

    [Benchmark(Baseline = true)]
    public Task<string> BareHandlerDispatch() => _bare.DispatchAsync<Ping, string>(_command, default);

    [Benchmark]
    public Task<string> PipelineFourBehaviors() => _pipeline.DispatchAsync<Ping, string>(_command, default);

    private static ICommandDispatcher Build(Action<IServiceCollection> configure) {
        var services = new ServiceCollection();
        services.AddTransient<ICommandHandler<Ping, string>, PingHandler>();
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        configure(services);

        return services.BuildServiceProvider().GetRequiredService<ICommandDispatcher>();
    }

    public sealed record Ping(string Name) : ICommand<string>;

    public sealed class PingHandler : ICommandHandler<Ping, string> {
        public Task<string> HandleAsync(Ping command, CancellationToken cancellationToken) =>
            Task.FromResult("pong");
    }

    public sealed class PassThroughBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse> {
        public Task<TResponse> HandleAsync(TRequest request, Func<TRequest, CancellationToken, Task<TResponse>> next, CancellationToken cancellationToken) =>
            next(request, cancellationToken);
    }
}
