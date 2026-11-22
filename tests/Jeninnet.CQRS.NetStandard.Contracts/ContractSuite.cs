//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.NetStandard.Contracts;

/// <summary>
/// Dispatch and caching-pipeline contract checks compiled for netstandard2.0. The modern test
/// runner calls these methods; the assembly itself consumes only the portable assets.
/// </summary>
public static class ContractSuite {
    /// <summary>Runs a command and a query through the portable dispatcher.</summary>
    /// <param name="cancellationToken">Cancels the dispatches.</param>
    public static async Task DispatchAsync(CancellationToken cancellationToken) {
        await using var provider = BuildProvider(static services => {
            services.AddTransient<ICommandHandler<ContractCommand, int>, ContractCommandHandler>();
            services.AddTransient<IQueryHandler<ContractQuery, string>, ContractQueryHandler>();
        });
        var broker = provider.GetRequiredService<ICqrsBroker>();

        var commandResult = await broker.DispatchAsync<ContractCommand, int>(new ContractCommand(21), cancellationToken).ConfigureAwait(false);
        var queryResult = await broker.DispatchAsync<ContractQuery, string>(new ContractQuery("contract"), cancellationToken).ConfigureAwait(false);

        commandResult.Should().Be(42);
        queryResult.Should().Be("contract");
    }

    /// <summary>Runs the caching pipeline behavior and asserts a cache hit.</summary>
    /// <param name="cancellationToken">Cancels the dispatches.</param>
    public static async Task CachingAsync(CancellationToken cancellationToken) {
        var executions = 0;
        await using var provider = BuildProvider(services => {
            services.AddTransient<IQueryHandler<CacheableContractQuery, string>>(_ =>
                new CacheableContractQueryHandler(() => Interlocked.Increment(ref executions)));
            services.AddCqrsCachingBehavior();
        });
        var broker = provider.GetRequiredService<ICqrsBroker>();

        var first = await broker.DispatchAsync<CacheableContractQuery, string>(new CacheableContractQuery("same"), cancellationToken).ConfigureAwait(false);
        var second = await broker.DispatchAsync<CacheableContractQuery, string>(new CacheableContractQuery("same"), cancellationToken).ConfigureAwait(false);

        first.Should().Be("value-1");
        second.Should().Be("value-1");
        executions.Should().Be(1);
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure) {
        var services = new ServiceCollection();
        services.AddSingleton<ICqrsBroker, CqrsBroker>();
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
        services.AddSingleton<IQueryDispatcher, QueryDispatcher>();
        configure(services);

        return services.BuildServiceProvider();
    }

    private sealed record ContractCommand(int Value) : ICommand<int>;

    private sealed class ContractCommandHandler : ICommandHandler<ContractCommand, int> {
        public Task<int> HandleAsync(ContractCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(command.Value * 2);
    }

    private sealed record ContractQuery(string Value) : IQuery<string>;

    private sealed class ContractQueryHandler : IQueryHandler<ContractQuery, string> {
        public Task<string> HandleAsync(ContractQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(query.Value);
    }

    private sealed record CacheableContractQuery(string CacheKey) : IQuery<string>, ICacheable<string> {
        public MemoryCacheEntryOptions? CacheOptions => new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) };
    }

    private sealed class CacheableContractQueryHandler(Func<int> next) : IQueryHandler<CacheableContractQuery, string> {
        public Task<string> HandleAsync(CacheableContractQuery query, CancellationToken cancellationToken) =>
            Task.FromResult($"value-{next()}");
    }
}