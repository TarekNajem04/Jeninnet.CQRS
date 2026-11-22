//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore.Tests;

/// <summary>
/// Characterization tests for CQRS-36 on the durable deferred row: a v1 row upgrades through the
/// registered upgrader before execution, and a row newer than the reader surfaces as an
/// unknown-version envelope for the pump's dead-letter path instead of being dropped.
/// </summary>
[TestClass]
public class EfMessageVersioningTests {
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public TestContext? TestContext { get; set; }

    private static string NewDbFile() => Path.Combine(Path.GetTempPath(), $"cqrs36-{Guid.NewGuid():N}.db");

    private static ServiceProvider BuildHost(string file, bool withUpgrader) {
        var services = new ServiceCollection();

        if(withUpgrader) {
            services.AddTransient<IMessageUpgrader, V1ToV2Upgrader>();
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
    public async Task V1Row_IsUpgradedBeforeTheEnvelopeIsReturnedAsync() {
        // Arrange: a v1 row whose Note is empty.
        var file = NewDbFile();
        await using var provider = BuildHost(file, withUpgrader: true);
        var now = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.Serialize(new VersionedMessage("order-1", null), _json);
        await AddRowAsync(provider, "v1", payload, 1, now);

        // Act
        var listed = await provider.GetRequiredService<IDeferredStore>().ListAsync(TestContext!.CancellationToken);

        // Assert
        var command = (VersionedMessage)listed.Single().Envelope.Command;
        MSTestAssertions.That(() => command.Note.Should().Be("upgraded"));
    }

    [TestMethod]
    public async Task NewerVersion_SurfacesAsUnknownVersionEnvelope_NotDroppedAsync() {
        // Arrange: version 99 cannot be read by a v2 reader.
        var file = NewDbFile();
        await using var provider = BuildHost(file, withUpgrader: true);
        var now = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.Serialize(new VersionedMessage("order-2", null), _json);
        await AddRowAsync(provider, "v99", payload, 99, now);

        // Act
        var listed = await provider.GetRequiredService<IDeferredStore>().ListAsync(TestContext!.CancellationToken);
        var envelope = listed.Single().Envelope;
        var thrown = await CaptureAsync(() => envelope.DispatchAsync(provider, TestContext!.CancellationToken));

        // Assert: the row exists as a typed failure, which the pump dead-letters.
        MSTestAssertions.That(() => envelope.Should().BeOfType<UnknownVersionEnvelope>());
        MSTestAssertions.That(() => thrown.Should().BeOfType<UnknownMessageVersionException>());
        MSTestAssertions.That(() => ((UnknownMessageVersionException)thrown!).StoredVersion.Should().Be(99));
    }

    private static async Task AddRowAsync(IServiceProvider provider, string key, string payload, int version, DateTimeOffset now) {
        var factory = provider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>();
        await using var db = await factory.CreateDbContextAsync();
        db.DeferredCommands.Add(new DeferredRecord {
            Key = key,
            CommandType = typeof(VersionedMessage).AssemblyQualifiedName ?? typeof(VersionedMessage).FullName ?? typeof(VersionedMessage).Name,
            Payload = payload,
            MessageVersion = version,
            DueUtc = now,
            StoredAtUtc = now,
        });
        await db.SaveChangesAsync();
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action) {
        try {
            await action().ConfigureAwait(false);

            return null;
        }
        catch(Exception exception) {
            return exception;
        }
    }

    public sealed record VersionedMessage(string Name, string? Note) : ICommand<string>;

    private sealed class V1ToV2Upgrader : MessageUpgrader<VersionedMessage> {
        public override int FromVersion => 1;

        public override int ToVersion => 2;

        protected override VersionedMessage UpgradeCore(VersionedMessage message) =>
            message with { Note = "upgraded" };
    }
}
