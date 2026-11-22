//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore.Tests;

/// <summary>
/// Characterization tests for CQRS-52 on EF Core: a tenant-scoped context cannot read another
/// tenant's rows, caller-supplied keys are composite with the tenant, and outbox rows retain
/// their tenant across a restart.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class EfTenantIsolationTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task IdempotencyRows_AreFilteredByTenant_AndAllowTheSameKeyPerTenantAsync() {
        // Arrange
        var file = NewDbFile();
        await using var provider = BuildProvider(file);
        var context = provider.GetRequiredService<ITenantContext>();
        var factory = provider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>();

        using(context.BeginScope("acme")) {
            await using var db = await factory.CreateDbContextAsync(TestContext!.CancellationToken).ConfigureAwait(false);
            db.Records.Add(Row("acme", "same-key", "acme-response"));
            await db.SaveChangesAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        }

        using(context.BeginScope("globex")) {
            await using var db = await factory.CreateDbContextAsync(TestContext!.CancellationToken).ConfigureAwait(false);
            db.Records.Add(Row("globex", "same-key", "globex-response"));
            await db.SaveChangesAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        }

        // Act
        var acme = await ListRowsAsync(factory, context, "acme", TestContext!.CancellationToken).ConfigureAwait(false);
        var globex = await ListRowsAsync(factory, context, "globex", TestContext!.CancellationToken).ConfigureAwait(false);
        var unscoped = await ListRowsAsync(factory, context, null, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert: the composite key permits the same caller key, and each scope sees only its row.
        MSTestAssertions.That(() => acme.Should().HaveCount(1));
        MSTestAssertions.That(() => acme[0].TenantId.Should().Be("acme"));
        MSTestAssertions.That(() => acme[0].Payload.Should().Be("acme-response"));
        MSTestAssertions.That(() => globex.Should().HaveCount(1));
        MSTestAssertions.That(() => globex[0].TenantId.Should().Be("globex"));
        MSTestAssertions.That(() => globex[0].Payload.Should().Be("globex-response"));
        MSTestAssertions.That(() => unscoped.Should().HaveCount(2));
    }

    [TestMethod]
    public async Task EfIdempotencyStore_KeepsTheSameKeySeparatePerTenantAsync() {
        // Arrange
        var file = NewDbFile();
        var services = new ServiceCollection();
        services.AddCqrsMultiTenancy(_ => null);
        services.AddCqrsEfIdempotencyStore(db => db.UseSqlite($"Data Source={file}"));
        await using var provider = services.BuildServiceProvider();
        var context = provider.GetRequiredService<ITenantContext>();
        var store = provider.GetRequiredService<IIdempotencyStore>();

        using(var scope = provider.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>().CreateDbContext();
            db.Database.EnsureCreated();
        }

        // Act: the same caller-supplied key under two tenants.
        using(context.BeginScope("acme")) {
            await store.StoreAsync("same-key", "acme-value", TestContext!.CancellationToken).ConfigureAwait(false);
        }

        using(context.BeginScope("globex")) {
            await store.StoreAsync("same-key", "globex-value", TestContext!.CancellationToken).ConfigureAwait(false);
        }

        string? acme;
        string? globex;

        using(context.BeginScope("acme")) {
            acme = await store.GetAsync<string>("same-key", TestContext!.CancellationToken).ConfigureAwait(false);
        }

        using(context.BeginScope("globex")) {
            globex = await store.GetAsync<string>("same-key", TestContext!.CancellationToken).ConfigureAwait(false);
        }

        // Assert
        MSTestAssertions.That(() => acme.Should().Be("acme-value"));
        MSTestAssertions.That(() => globex.Should().Be("globex-value"));
    }

    [TestMethod]
    public async Task SchedulerRows_UseCompositeTenantKeysAsync() {
        // Arrange
        var file = NewDbFile();
        var services = new ServiceCollection();
        services.AddCqrsMultiTenancy(_ => null);
        services.AddSingleton<IMessageTransport, DropTransport>();
        services.AddCqrsEfScheduler(db => db.UseSqlite($"Data Source={file}"));
        await using var provider = services.BuildServiceProvider();
        var context = provider.GetRequiredService<ITenantContext>();
        var store = provider.GetRequiredService<ISchedulerStore>();

        using(var scope = provider.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>().CreateDbContext();
            db.Database.EnsureCreated();
        }

        var due = TimeProvider.System.GetUtcNow().AddMinutes(1);

        // Act: the same caller-supplied schedule key in two tenants.
        using(context.BeginScope("acme")) {
            var added = await store.TryAddAsync(Schedule("acme", "same", due), TestContext!.CancellationToken).ConfigureAwait(false);
            MSTestAssertions.That(() => added.Should().BeTrue());
        }

        using(context.BeginScope("globex")) {
            var added = await store.TryAddAsync(Schedule("globex", "same", due), TestContext!.CancellationToken).ConfigureAwait(false);
            MSTestAssertions.That(() => added.Should().BeTrue());
        }

        var acme = await ListSchedulesAsync(store, context, "acme", TestContext!.CancellationToken).ConfigureAwait(false);
        var globex = await ListSchedulesAsync(store, context, "globex", TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => acme.Should().HaveCount(1));
        MSTestAssertions.That(() => acme[0].TenantId.Should().Be("acme"));
        MSTestAssertions.That(() => globex.Should().HaveCount(1));
        MSTestAssertions.That(() => globex[0].TenantId.Should().Be("globex"));
    }

    [TestMethod]
    public async Task OutboxRows_KeepTheirTenant_AcrossRestartAndScopeAsync() {
        // Arrange
        var file = NewDbFile();

        await using(var first = BuildProvider(file)) {
            var context = first.GetRequiredService<ITenantContext>();
            var store = first.GetRequiredService<IOutboxStore>();

            using(context.BeginScope("acme")) {
                await store.StageAsync(
                    new OutboxNotification<TenantNotification>(new TenantNotification("acme-event"), TimeProvider.System.GetUtcNow()),
                    TestContext!.CancellationToken).ConfigureAwait(false);
            }

            using(context.BeginScope("globex")) {
                await store.StageAsync(
                    new OutboxNotification<TenantNotification>(new TenantNotification("globex-event"), TimeProvider.System.GetUtcNow()),
                    TestContext!.CancellationToken).ConfigureAwait(false);
            }
        }

        // Act: a fresh host over the same database.
        await using var second = BuildProvider(file);
        var restartedContext = second.GetRequiredService<ITenantContext>();
        var restartedStore = second.GetRequiredService<IOutboxStore>();

        var acmePending = await ListOutboxAsync(restartedStore, restartedContext, "acme", TestContext!.CancellationToken).ConfigureAwait(false);
        var globexPending = await ListOutboxAsync(restartedStore, restartedContext, "globex", TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => acmePending.Should().HaveCount(1));
        MSTestAssertions.That(() => acmePending[0].TenantId.Should().Be("acme"));
        MSTestAssertions.That(() => ((TenantNotification)acmePending[0].Command).Name.Should().Be("acme-event"));
        MSTestAssertions.That(() => globexPending.Should().HaveCount(1));
        MSTestAssertions.That(() => globexPending[0].TenantId.Should().Be("globex"));
        MSTestAssertions.That(() => ((TenantNotification)globexPending[0].Command).Name.Should().Be("globex-event"));
    }

    private static ServiceProvider BuildProvider(string file) {
        var services = new ServiceCollection();
        services.AddCqrsMultiTenancy(_ => null);
        services.AddCqrsEfNotificationOutbox(db => db.UseSqlite($"Data Source={file}"));
        var provider = services.BuildServiceProvider();

        using(var scope = provider.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>().CreateDbContext();
            db.Database.EnsureCreated();
        }

        return provider;
    }

    private static IdempotencyRecord Row(string tenantId, string key, string payload) =>
        new() {
            TenantId = tenantId,
            Key = key,
            ResponseType = typeof(string).AssemblyQualifiedName ?? nameof(String),
            Payload = payload,
            StoredAtUtc = TimeProvider.System.GetUtcNow(),
        };

    private static async Task<IReadOnlyList<IdempotencyRecord>> ListRowsAsync(
        IDbContextFactory<IdempotencyDbContext> factory,
        ITenantContext context,
        string? tenantId,
        CancellationToken cancellationToken) {
        using(context.EnterScope(tenantId)) {
            await using var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            return await db.Records.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static ScheduledMessage Schedule(string tenantId, string key, DateTimeOffset dueUtc) =>
        new(
            key,
            typeof(TenantCommand).AssemblyQualifiedName ?? typeof(TenantCommand).FullName ?? typeof(TenantCommand).Name,
            "application/json",
            [],
            ScheduledMessageKind.OneShot,
            dueUtc,
            TimeProvider.System.GetUtcNow(),
            null,
            null,
            0,
            0,
            tenantId);

    private static async Task<IReadOnlyList<ScheduledMessage>> ListSchedulesAsync(
        ISchedulerStore store,
        ITenantContext context,
        string tenantId,
        CancellationToken cancellationToken) {
        using(context.BeginScope(tenantId)) {
            return await store.ListAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<IReadOnlyList<IDeferredEnvelope>> ListOutboxAsync(
        IOutboxStore store,
        ITenantContext context,
        string tenantId,
        CancellationToken cancellationToken) {
        using(context.BeginScope(tenantId)) {
            var pending = await store.ListAsync(cancellationToken).ConfigureAwait(false);

            return pending.Select(static pair => pair.Notification).ToList();
        }
    }

    private sealed record TenantNotification(string Name) : INotification;

    private sealed record TenantCommand(string Name) : ICommand;

    private sealed class DropTransport : IMessageTransport {
        public Task SendAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task PublishAsync(object message, Type messageType, TransportMessageOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static string NewDbFile() =>
        Path.Combine(Path.GetTempPath(), $"cqrs52-tenant-{Guid.NewGuid():N}.db");
}