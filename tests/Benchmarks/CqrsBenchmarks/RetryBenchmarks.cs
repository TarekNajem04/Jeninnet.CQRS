//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Benchmarks;

/// <summary>
/// Retry path: the handler fails once, then succeeds on the second attempt with zero delay.
/// </summary>
[MemoryDiagnoser]
public class RetryBenchmarks {
    private ICommandDispatcher _dispatcher = null!;
    private FlakyHandler _handler = null!;
    private readonly Ping _command = new("bench");

    [GlobalSetup]
    public void Setup() {
        _handler = new FlakyHandler();
        var services = new ServiceCollection();
        services.AddSingleton(_handler);
        services.AddSingleton<ICommandHandler<Ping, string>>(static provider => provider.GetRequiredService<FlakyHandler>());
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddSingleton<IOptions<RetryBehaviorOptions>>(Options.Create(new RetryBehaviorOptions {
            RetryCount = 3,
            BaseDelayMilliseconds = 0,
        }));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(RetryBehavior<,>));
        _dispatcher = services.BuildServiceProvider().GetRequiredService<ICommandDispatcher>();
    }

    [Benchmark]
    public Task<string> RetryAfterOneFailure() {
        _handler.Attempts = 0;

        return _dispatcher.DispatchAsync<Ping, string>(_command, default);
    }

    public sealed record Ping(string Name) : ICommand<string>;

    public sealed class FlakyHandler : ICommandHandler<Ping, string> {
        public int Attempts { get; set; }

        public Task<string> HandleAsync(Ping command, CancellationToken cancellationToken) {
            Attempts++;

            return Attempts is 1
                ? throw new TimeoutException("first attempt fails")
                : Task.FromResult("pong");
        }
    }
}
