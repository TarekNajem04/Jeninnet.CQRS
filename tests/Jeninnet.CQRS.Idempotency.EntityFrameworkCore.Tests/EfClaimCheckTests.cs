//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore.Tests;

/// <summary>
/// Characterization tests for CQRS-28 on the EF Core stores: large payloads move to the
/// claim-check table and rows keep a reference, small payloads stay inline, custom serializers
/// round-trip across restarts (including binary output), and idempotency overwrites reclaim the
/// previous claim.
/// </summary>
[TestClass]
public class EfClaimCheckTests {
    public TestContext? TestContext { get; set; }

    private static string NewDbFile() => Path.Combine(Path.GetTempPath(), $"cqrs28-{Guid.NewGuid():N}.db");

    private static ServiceProvider BuildHost(
        string file,
        int thresholdBytes,
        IMessageSerializer? serializer = null,
        Action<EfIdempotencyStoreOptions>? configureIdempotency = null) {
        var services = new ServiceCollection();

        if(serializer is not null) {
            services.AddSingleton(serializer);
        }

        services.AddCqrsEfDeferredDelivery(builder => builder.UseSqlite($"Data Source={file}"));
        services.AddCqrsEfIdempotencyStore(configureStore: configureIdempotency);
        services.AddCqrsEfClaimCheck(configure: options => options.ThresholdBytes = thresholdBytes);
        var provider = services.BuildServiceProvider();

        using(var scope = provider.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>().CreateDbContext();
            db.Database.EnsureCreated();
        }

        return provider;
    }

    [TestMethod]
    public async Task LargeDeferredPayload_UsesClaimCheck_AndSurvivesRestartAsync() {
        // Arrange
        var file = NewDbFile();
        var message = new LargeCommand(new string('x', 2048));

        await using(var first = BuildHost(file, 256)) {
            var store = first.GetRequiredService<IDeferredStore>();
            await store.StageAsync(new DeferredEnvelope<LargeCommand>(message, DateTimeOffset.UtcNow, null), TestContext!.CancellationToken);

            // Assert: the row is a reference, and the payload exists once in the claim table.
            var factory = first.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>();
            await using var db = await factory.CreateDbContextAsync(TestContext!.CancellationToken);
            var record = await db.DeferredCommands.AsNoTracking().SingleAsync(TestContext!.CancellationToken);
            var claims = await db.ClaimChecks.CountAsync(TestContext!.CancellationToken);

            MSTestAssertions.That(() => record.ClaimCheckKey.Should().NotBeNull());
            MSTestAssertions.That(() => record.Payload.Should().BeEmpty());
            MSTestAssertions.That(() => claims.Should().Be(1));
        }

        // Act: a fresh host over the same file resolves the reference.
        await using var second = BuildHost(file, 256);
        var listed = await second.GetRequiredService<IDeferredStore>().ListAsync(TestContext!.CancellationToken);
        var restored = (LargeCommand)listed.Single().Envelope.Command;

        // Assert
        MSTestAssertions.That(() => restored.Payload.Should().Be(message.Payload));
    }

    [TestMethod]
    public async Task SmallDeferredPayload_StaysInlineAsync() {
        // Arrange
        await using var provider = BuildHost(NewDbFile(), 256);
        var store = provider.GetRequiredService<IDeferredStore>();
        var message = new LargeCommand("tiny");
        await store.StageAsync(new DeferredEnvelope<LargeCommand>(message, DateTimeOffset.UtcNow, null), TestContext!.CancellationToken);

        // Act
        var factory = provider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>();
        await using var db = await factory.CreateDbContextAsync(TestContext!.CancellationToken);
        var record = await db.DeferredCommands.AsNoTracking().SingleAsync(TestContext!.CancellationToken);
        var claims = await db.ClaimChecks.CountAsync(TestContext!.CancellationToken);
        var listed = await store.ListAsync(TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => record.ClaimCheckKey.Should().BeNull());
        MSTestAssertions.That(() => record.Payload.Should().NotBeEmpty());
        MSTestAssertions.That(() => claims.Should().Be(0));
        MSTestAssertions.That(() => ((LargeCommand)listed.Single().Envelope.Command).Payload.Should().Be("tiny"));
    }

    [TestMethod]
    public async Task CustomSerializer_IsHonored_AcrossRestartAsync() {
        // Arrange: a binary prefix forces the inline Base64 shape.
        var file = NewDbFile();
        var message = new LargeCommand("custom");

        await using(var first = BuildHost(file, 1_000_000, new PrefixedSerializer())) {
            var store = first.GetRequiredService<IDeferredStore>();
            await store.StageAsync(new DeferredEnvelope<LargeCommand>(message, DateTimeOffset.UtcNow, null), TestContext!.CancellationToken);

            var factory = first.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>();
            await using var db = await factory.CreateDbContextAsync(TestContext!.CancellationToken);
            var record = await db.DeferredCommands.AsNoTracking().SingleAsync(TestContext!.CancellationToken);

            MSTestAssertions.That(() => record.ClaimCheckKey.Should().BeNull());
            MSTestAssertions.That(() => record.Payload.Should().StartWith("~b64~"));
        }

        // Act: the same custom serializer on a fresh host must read the bytes back.
        await using var second = BuildHost(file, 1_000_000, new PrefixedSerializer());
        var listed = await second.GetRequiredService<IDeferredStore>().ListAsync(TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => ((LargeCommand)listed.Single().Envelope.Command).Payload.Should().Be("custom"));
    }

    [TestMethod]
    public async Task IdempotencyClaimCheck_RoundTrips_AndReclaimsOnOverwriteAsync() {
        // Arrange
        await using var provider = BuildHost(NewDbFile(), 64, configureIdempotency: static options => options.TimeToLive = TimeSpan.FromHours(1));
        var store = provider.GetRequiredService<IIdempotencyStore>();
        var firstPayload = new string('a', 512);
        var secondPayload = new string('b', 512);
        var factory = provider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>();

        // Act
        await store.StoreAsync("key", firstPayload, TestContext!.CancellationToken);
        var firstKey = await ReadClaimKeyAsync(factory, TestContext!.CancellationToken);
        var firstRoundTrip = await store.GetAsync<string>("key", TestContext!.CancellationToken);

        await store.StoreAsync("key", secondPayload, TestContext!.CancellationToken);
        var secondKey = await ReadClaimKeyAsync(factory, TestContext!.CancellationToken);
        var secondRoundTrip = await store.GetAsync<string>("key", TestContext!.CancellationToken);

        // Assert: one live claim, the old one was reclaimed, and both reads round-trip.
        MSTestAssertions.That(() => firstRoundTrip.Should().Be(firstPayload));
        MSTestAssertions.That(() => secondRoundTrip.Should().Be(secondPayload));
        MSTestAssertions.That(() => firstKey.Should().NotBeNull());
        MSTestAssertions.That(() => secondKey.Should().NotBe(firstKey));

        await using var db = await factory.CreateDbContextAsync(TestContext!.CancellationToken);
        var claims = await db.ClaimChecks.CountAsync(TestContext!.CancellationToken);
        MSTestAssertions.That(() => claims.Should().Be(1));
    }

    private static async Task<string?> ReadClaimKeyAsync(IDbContextFactory<IdempotencyDbContext> factory, CancellationToken cancellationToken) {
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var record = await db.Records.AsNoTracking().SingleAsync(cancellationToken);

        return record.ClaimCheckKey;
    }

    private sealed record LargeCommand(string Payload) : ICommand;

    private sealed class PrefixedSerializer : IMessageSerializer {
        private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

        public string ContentType => "application/x-prefixed+json";

        public byte[] Serialize(object message, Type messageType) {
            var json = JsonSerializer.SerializeToUtf8Bytes(message, messageType, _json);
            var prefixed = new byte[json.Length + 1];
            prefixed[0] = 0xAB;
            json.AsSpan().CopyTo(prefixed.AsSpan(1));

            return prefixed;
        }

        public object? Deserialize(ReadOnlySpan<byte> payload, Type messageType) {
            if(payload.Length is 0 || payload[0] is not 0xAB) {
                throw new InvalidOperationException("Payload does not carry the custom prefix.");
            }

            return JsonSerializer.Deserialize(payload[1..], messageType, _json);
        }
    }
}
