//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore.Tests;

/// <summary>
/// CQRS-39 characterization for the EF store: the generated factory (represented here by a
/// recording double) is consulted before reflection, applies upgrades through <c>TryWrap</c>,
/// and still lets unknown version rows reach the dead-letter path.
/// </summary>
[TestClass]
public class EfEnvelopeFactoryTests {
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public TestContext? TestContext { get; set; }

    private static string NewDbFile() => Path.Combine(Path.GetTempPath(), $"cqrs39-{Guid.NewGuid():N}.db");

    private static ServiceProvider BuildHost(string file, IDeferredEnvelopeFactory? factory = null, bool withUpgrader = false) {
        var services = new ServiceCollection();

        if(withUpgrader) {
            services.AddTransient<IMessageUpgrader, V1ToV2Upgrader>();
        }

        if(factory is not null) {
            services.AddSingleton(factory);
        }

        services.AddCqrsEfDeferredDelivery(builder => builder.UseSqlite($"Data Source={file}"));
        var provider = services.BuildServiceProvider();

        using(var scope = provider.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>().CreateDbContext();
            db.Database.EnsureCreated();
        }

        return provider;
    }

    [TestMethod]
    public async Task FactoryFirst_RebuildsKnownCommands_WithoutReflectionAsync() {
        // Arrange: the store has a factory that handles this command type.
        var factory = new RecordingFactory();
        await using var provider = BuildHost(NewDbFile(), factory);
        var store = provider.GetRequiredService<IDeferredStore>();
        var due = DateTimeOffset.UtcNow;
        await store.StageAsync(new DeferredEnvelope<FactoryCommand>(new FactoryCommand("one"), due, null), TestContext!.CancellationToken);

        // Act
        var listed = await store.ListAsync(TestContext!.CancellationToken);

        // Assert: the factory's envelope (the tag) proves reflection was not used.
        var envelope = listed.Single().Envelope;
        MSTestAssertions.That(() => envelope.Should().BeOfType<TaggedEnvelope>());
        MSTestAssertions.That(() => ((FactoryCommand)envelope.Command).Name.Should().Be("one"));
        MSTestAssertions.That(() => factory.CreateCalls.Should().Be(1));
    }

    [TestMethod]
    public async Task FactoryMiss_FallsBackToTheReflectionPathAsync() {
        // Arrange: the factory only knows other command types.
        var factory = new RecordingFactory();
        await using var provider = BuildHost(NewDbFile(), factory);
        var store = provider.GetRequiredService<IDeferredStore>();
        await store.StageAsync(new DeferredEnvelope<ReflectionOnlyCommand>(new ReflectionOnlyCommand("fallback"), DateTimeOffset.UtcNow, null), TestContext!.CancellationToken);

        // Act
        var listed = await store.ListAsync(TestContext!.CancellationToken);

        // Assert: the annotated fallback rebuilt the untagged envelope.
        var envelope = listed.Single().Envelope;
        MSTestAssertions.That(() => envelope.Should().BeOfType<DeferredEnvelope<ReflectionOnlyCommand>>());
        MSTestAssertions.That(() => factory.CreateCalls.Should().Be(1));
    }

    [TestMethod]
    public async Task FactoryPath_AppliesTheUpgradeBeforeReturningAsync() {
        // Arrange: a v1 row with an upgrader that replaces the message instance.
        var factory = new RecordingFactory();
        await using var provider = BuildHost(NewDbFile(), factory, withUpgrader: true);
        await AddRowAsync(provider, new FactoryVersionedCommand("order-1", null), version: 1);

        // Act
        var listed = await provider.GetRequiredService<IDeferredStore>().ListAsync(TestContext!.CancellationToken);

        // Assert: the upgraded command went back through the factory's wrap path.
        var envelope = listed.Single().Envelope;
        MSTestAssertions.That(() => envelope.Should().BeOfType<TaggedEnvelope>());
        MSTestAssertions.That(() => ((FactoryVersionedCommand)envelope.Command).Note.Should().Be("upgraded"));
        MSTestAssertions.That(() => factory.WrapCalls.Should().Be(1));
    }

    [TestMethod]
    public async Task FactoryPath_NewerVersion_SurfacesAsUnknownVersionEnvelopeAsync() {
        // Arrange
        var factory = new RecordingFactory();
        await using var provider = BuildHost(NewDbFile(), factory, withUpgrader: true);
        await AddRowAsync(provider, new FactoryVersionedCommand("order-2", null), version: 99);

        // Act
        var listed = await provider.GetRequiredService<IDeferredStore>().ListAsync(TestContext!.CancellationToken);

        // Assert: the pump can dead-letter it instead of dropping the ghost.
        var envelope = listed.Single().Envelope;
        MSTestAssertions.That(() => envelope.Should().BeOfType<UnknownVersionEnvelope>());
        MSTestAssertions.That(() => factory.WrapCalls.Should().Be(0));
    }

    private static async Task AddRowAsync(IServiceProvider provider, object command, int version) {
        var serializer = provider.GetRequiredService<IMessageSerializer>();
        var commandType = command.GetType();
        var factory = provider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        db.DeferredCommands.Add(new DeferredRecord {
            Key = Guid.NewGuid().ToString("N"),
            CommandType = commandType.AssemblyQualifiedName ?? commandType.FullName ?? commandType.Name,
            Payload = Encoding.UTF8.GetString(serializer.Serialize(command, commandType)),
            MessageVersion = version,
            DueUtc = DateTimeOffset.UtcNow,
            StoredAtUtc = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    public sealed record FactoryCommand(string Name) : ICommand;

    public sealed record ReflectionOnlyCommand(string Name) : ICommand;

    public sealed record FactoryVersionedCommand(string Name, string? Note) : ICommand<string>;

    private sealed class V1ToV2Upgrader : MessageUpgrader<FactoryVersionedCommand> {
        public override int FromVersion => 1;

        public override int ToVersion => 2;

        protected override FactoryVersionedCommand UpgradeCore(FactoryVersionedCommand message) =>
            message with { Note = "upgraded" };
    }

    private sealed class TaggedEnvelope(IDeferredEnvelope inner) : IDeferredEnvelope {
        public object Command => inner.Command;

        public int Version => inner.Version;

        public DateTimeOffset DueUtc => inner.DueUtc;

        public DateTimeOffset? ExpiresAtUtc => inner.ExpiresAtUtc;

        public string? TenantId => inner.TenantId;

        public IDeferredEnvelope WithTenant(string? tenantId) => new TaggedEnvelope(inner.WithTenant(tenantId));

        public Task DispatchAsync(IServiceProvider services, CancellationToken cancellationToken) =>
            inner.DispatchAsync(services, cancellationToken);
    }

    private sealed class RecordingFactory : IDeferredEnvelopeFactory {
        public int CreateCalls { get; private set; }

        public int WrapCalls { get; private set; }

        public bool TryCreate(
            string commandTypeName,
            ReadOnlySpan<byte> payload,
            DateTimeOffset dueUtc,
            DateTimeOffset? expiresAtUtc,
            int version,
            RecurringEnvelopeMetadata? recurring,
            [NotNullWhen(true)] out IDeferredEnvelope? envelope) {
            CreateCalls++;
            envelope = null;

            if(IsType<FactoryCommand>(commandTypeName)) {
                var command = JsonSerializer.Deserialize<FactoryCommand>(payload, _json);

                if(command is null) {
                    return false;
                }

                envelope = new TaggedEnvelope(new DeferredEnvelope<FactoryCommand>(command, dueUtc, expiresAtUtc, version));

                return true;
            }

            if(IsType<FactoryVersionedCommand>(commandTypeName)) {
                var command = JsonSerializer.Deserialize<FactoryVersionedCommand>(payload, _json);

                if(command is null) {
                    return false;
                }

                envelope = new TaggedEnvelope(new DeferredResultEnvelope<FactoryVersionedCommand, string>(command, dueUtc, expiresAtUtc, version));

                return true;
            }

            return false;
        }

        public bool TryWrap(
            object command,
            DateTimeOffset dueUtc,
            DateTimeOffset? expiresAtUtc,
            int version,
            RecurringEnvelopeMetadata? recurring,
            [NotNullWhen(true)] out IDeferredEnvelope? envelope) {
            WrapCalls++;

            switch(command) {
                case FactoryCommand typed:
                    envelope = new TaggedEnvelope(new DeferredEnvelope<FactoryCommand>(typed, dueUtc, expiresAtUtc, version));

                    return true;
                case FactoryVersionedCommand typed:
                    envelope = new TaggedEnvelope(new DeferredResultEnvelope<FactoryVersionedCommand, string>(typed, dueUtc, expiresAtUtc, version));

                    return true;
                default:
                    envelope = null;

                    return false;
            }
        }

        private static bool IsType<T>(string commandTypeName) =>
            commandTypeName == typeof(T).FullName || commandTypeName == typeof(T).AssemblyQualifiedName;
    }
}
