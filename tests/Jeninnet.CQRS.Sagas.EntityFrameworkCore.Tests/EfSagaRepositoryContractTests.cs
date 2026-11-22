//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.EntityFrameworkCore.Tests;

/// <summary>Runs the shared repository contract against the EF Core saga store.</summary>
[TestClass]
public sealed class EfSagaRepositoryContractTests : SagaRepositoryContractTests {
    private ServiceProvider? _provider;

    /// <inheritdoc />
    protected override ISagaRepository<SagaRepositoryTestState> CreateRepository() {
        var file = Path.Combine(Path.GetTempPath(), $"cqrs61-saga-repo-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddDbContextFactory<SagaDbContext>(db => db.UseSqlite($"Data Source={file}"));
        services.AddSingleton<IMessageSerializer>(new JsonMessageSerializer(Options.Create(new JsonMessageSerializerOptions())));
        services.AddSingleton(TimeProvider.System);
        _provider = services.BuildServiceProvider();

        using(var scope = _provider.CreateScope()) {
            var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SagaDbContext>>();
            using var db = factory.CreateDbContext();
            db.Database.EnsureCreated();
        }

        var store = new EfSagaStore(
            _provider.GetRequiredService<IDbContextFactory<SagaDbContext>>(),
            _provider.GetRequiredService<IMessageSerializer>());

        return new SagaRepository<SagaRepositoryTestState>(store);
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeRepositoryAsync() {
        if(_provider is not null) {
            await _provider.DisposeAsync().ConfigureAwait(false);
            _provider = null;
        }
    }
}
