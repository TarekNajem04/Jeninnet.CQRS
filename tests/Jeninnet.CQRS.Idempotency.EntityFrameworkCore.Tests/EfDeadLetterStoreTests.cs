//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore.Tests;

/// <summary>
/// Characterization tests for CQRS-26 on the EF Core store: dead letters survive a restart,
/// attempt counts and last errors persist, replay runs the original command and removes the
/// entry, and retention pruning works against the real database.
/// </summary>
[TestClass]
public class EfDeadLetterStoreTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task EfDeadLetter_RemoveGhostAndPruneBranchesAsync() {
        // Arrange
        var file = NewDbFile();
        var now = DateTimeOffset.UtcNow;
        var cancellationToken = TestContext!.CancellationToken;
        await using var host = BuildHost(file, [], options => {
            options.MaxEntries = 3;
            options.Retention = TimeSpan.FromDays(1);
        });
        var store = host.GetRequiredService<IDeadLetterStore>();
        var factory = host.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>();

        // Missing rows are a no-op; a real row is removed with its claim check.
        await store.RemoveAsync("missing", cancellationToken).ConfigureAwait(false);
        var removeKey = await store.RecordAsync(
            new DeferredEnvelope<DeadLetterPing>(new DeadLetterPing("remove"), now, null),
            new InvalidOperationException("remove"),
            now,
            cancellationToken).ConfigureAwait(false);
        Assert.IsNotNull(await store.GetAsync(removeKey, cancellationToken).ConfigureAwait(false));
        await store.RemoveAsync(removeKey, cancellationToken).ConfigureAwait(false);
        Assert.IsNull(await store.GetAsync(removeKey, cancellationToken).ConfigureAwait(false));

        // A corrupt payload for a known type is a ghost and is dropped from list.
        await using(var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false)) {
            db.DeadLetters.Add(NewGhost("ghost-list", now));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        Assert.AreEqual(0, (await store.ListAsync(cancellationToken).ConfigureAwait(false)).Count);

        // The same ghost is dropped when fetched directly.
        await using(var db = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false)) {
            db.DeadLetters.Add(NewGhost("ghost-get", now));
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        Assert.IsNull(await store.GetAsync("ghost-get", cancellationToken).ConfigureAwait(false));

        // Retention deletes expired rows while the entry cap has room.
        var oldKey = await store.RecordAsync(
            new DeferredEnvelope<DeadLetterPing>(new DeadLetterPing("old"), now.AddDays(-10), null),
            new InvalidOperationException("old"),
            now.AddDays(-10),
            cancellationToken).ConfigureAwait(false);
        var freshKey = await store.RecordAsync(
            new DeferredEnvelope<DeadLetterPing>(new DeadLetterPing("fresh"), now, null),
            new InvalidOperationException("fresh"),
            now,
            cancellationToken).ConfigureAwait(false);
        var secondKey = await store.RecordAsync(
            new DeferredEnvelope<DeadLetterPing>(new DeadLetterPing("second"), now.AddMinutes(1), null),
            new InvalidOperationException("second"),
            now.AddMinutes(1),
            cancellationToken).ConfigureAwait(false);

        var expired = await store.PruneAsync(now, cancellationToken).ConfigureAwait(false);

        Assert.AreEqual(1, expired);
        Assert.IsNull(await store.GetAsync(oldKey, cancellationToken).ConfigureAwait(false));
        Assert.IsNotNull(await store.GetAsync(freshKey, cancellationToken).ConfigureAwait(false));
        Assert.IsNotNull(await store.GetAsync(secondKey, cancellationToken).ConfigureAwait(false));

        // A smaller entry cap evicts the oldest survivor on the next sweep.
        await using var capped = BuildHost(file, [], options => {
            options.MaxEntries = 1;
            options.Retention = TimeSpan.FromDays(1);
        });
        var cappedStore = capped.GetRequiredService<IDeadLetterStore>();
        var evicted = await cappedStore.PruneAsync(now, cancellationToken).ConfigureAwait(false);

        // Assert
        Assert.AreEqual(1, evicted);
        Assert.IsNull(await cappedStore.GetAsync(freshKey, cancellationToken).ConfigureAwait(false));
        Assert.IsNotNull(await cappedStore.GetAsync(secondKey, cancellationToken).ConfigureAwait(false));
        Assert.AreEqual(1, (await cappedStore.ListAsync(cancellationToken).ConfigureAwait(false)).Count);
    }

    private static DeadLetterRecord NewGhost(string key, DateTimeOffset now) =>
        new() {
            TenantId = string.Empty,
            Key = key,
            CommandType = typeof(DeadLetterPing).AssemblyQualifiedName ?? nameof(DeadLetterPing),
            Payload = "{",
            DueUtc = now,
            FirstFailedAtUtc = now,
            LastFailedAtUtc = now,
            LastErrorType = "Missing.Error",
            LastErrorMessage = "ghost",
        };

    private static string NewDbFile() => Path.Combine(Path.GetTempPath(), $"cqrs26-{Guid.NewGuid():N}.db");

    private static ServiceProvider BuildHost(string file, List<string> calls, Action<DeadLetterOptions>? configureDeadLetters = null) {
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddTransient<ICommandHandler<DeadLetterPing>, DeadLetterPingHandler>();
        services.AddCqrsEfDeferredDelivery(builder => builder.UseSqlite($"Data Source={file}"));

        if(configureDeadLetters is not null) {
            services.Configure(configureDeadLetters);
        }

        var provider = services.BuildServiceProvider();

        using(var scope = provider.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>().CreateDbContext();
            db.Database.EnsureCreated();
        }

        return provider;
    }

    private static async Task SpinUntilAsync(Func<Task<bool>> condition, string what, int timeoutMilliseconds = 5000) {
        var sw = Stopwatch.StartNew();

        while(!await condition().ConfigureAwait(false)) {
            if(sw.ElapsedMilliseconds > timeoutMilliseconds) {
                throw new TimeoutException($"timed out waiting for: {what}");
            }

            await Task.Delay(25).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task DeadLetter_RoundTrip_SurvivesRestartAsync() {
        // Arrange: record, then drop the whole host.
        var file = NewDbFile();
        string key;

        await using(var first = BuildHost(file, [])) {
            var store = first.GetRequiredService<IDeadLetterStore>();
            key = await store.RecordAsync(
                new DeferredEnvelope<DeadLetterPing>(new DeadLetterPing("boom"), DateTimeOffset.UtcNow, null),
                new InvalidOperationException("kaboom"),
                DateTimeOffset.UtcNow,
                TestContext!.CancellationToken);
        }

        // Act: a fresh host over the same file must still find it.
        await using var second = BuildHost(file, []);
        var entry = await second.GetRequiredService<IDeadLetterStore>().GetAsync(key, TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => entry.Should().NotBeNull());
        var found = entry!;
        MSTestAssertions.That(() => found.Attempts.Should().Be(1));
        MSTestAssertions.That(() => found.LastErrorMessage.Should().Be("kaboom"));
        MSTestAssertions.That(() => found.LastErrorType.Should().Contain(nameof(InvalidOperationException)));
        MSTestAssertions.That(() => found.CommandType.Should().Contain(nameof(DeadLetterPing)));
        MSTestAssertions.That(() => ((DeadLetterPing)found.Envelope.Command).Name.Should().Be("boom"));
    }

    [TestMethod]
    public async Task TransportEnvelope_DeadLetter_RoundTripsAsInspectablePoisonAsync() {
        // Arrange: an unreadable envelope with an unknown content type.
        var file = NewDbFile();
        var envelope = new TransportEnvelope(
            "message-1",
            "My.Message, My.Assembly",
            "application/x-protobuf",
            new byte[] { 1, 2, 3 },
            null,
            null,
            null,
            null,
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(StringComparer.Ordinal) { [TransportHeaders.MessageVersion] = "2" },
            new TransportTopology("exchange", "queue", "topic", "routing-key"),
            TransportDeliveryMode.Send) {
            MessageVersion = 2,
        };
        string key;

        await using(var first = BuildHost(file, [])) {
            key = await first.GetRequiredService<IDeadLetterStore>().RecordAsync(
                new TransportEnvelopeDeadLetter(envelope),
                new UnknownContentTypeException("application/x-protobuf", ["application/json"]),
                DateTimeOffset.UtcNow,
                TestContext!.CancellationToken);
        }

        // Act: a fresh host rebuilds the poison as a transport dead letter, not a replayable command.
        await using var second = BuildHost(file, []);
        var entry = await second.GetRequiredService<IDeadLetterStore>().GetAsync(key, TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => entry.Should().NotBeNull());
        MSTestAssertions.That(() => entry!.Envelope.Should().BeOfType<TransportEnvelopeDeadLetter>());
        var poison = (TransportEnvelopeDeadLetter)entry!.Envelope;
        MSTestAssertions.That(() => poison.Envelope.MessageType.Should().Be("My.Message, My.Assembly"));
        MSTestAssertions.That(() => poison.Envelope.MessageVersion.Should().Be(2));
        MSTestAssertions.That(() => entry!.CommandType.Should().Be("My.Message, My.Assembly"));
        MSTestAssertions.That(() => entry!.LastErrorType.Should().Contain(nameof(UnknownContentTypeException)));

        Exception? thrown = null;

        try {
            await poison.DispatchAsync(second, default).ConfigureAwait(false);
        }
        catch(Exception exception) {
            thrown = exception;
        }

        MSTestAssertions.That(() => thrown.Should().BeOfType<InvalidOperationException>());
        MSTestAssertions.That(() => thrown!.Message.Should().Contain("cannot be replayed"));
    }

    [TestMethod]
    public async Task RecordFailure_PersistsAttemptsAndLastErrorAsync() {
        // Arrange
        await using var provider = BuildHost(NewDbFile(), []);
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var key = await store.RecordAsync(
            new DeferredEnvelope<DeadLetterPing>(new DeadLetterPing("x"), DateTimeOffset.UtcNow, null),
            new InvalidOperationException("first"),
            DateTimeOffset.UtcNow,
            TestContext!.CancellationToken);

        // Act: two replayed failures against the same entry.
        await store.RecordFailureAsync(key, new TimeoutException("second"), DateTimeOffset.UtcNow, TestContext!.CancellationToken);
        await store.RecordFailureAsync(key, new TimeoutException("third"), DateTimeOffset.UtcNow, TestContext!.CancellationToken);

        // Assert
        var entry = await store.GetAsync(key, TestContext!.CancellationToken);
        var found = entry!;
        MSTestAssertions.That(() => found.Attempts.Should().Be(3));
        MSTestAssertions.That(() => found.LastErrorType.Should().Contain(nameof(TimeoutException)));
        MSTestAssertions.That(() => found.LastErrorMessage.Should().Be("third"));
    }

    [TestMethod]
    public async Task Replay_RunsOriginalCommand_AndRemovesEntryAsync() {
        // Arrange
        var calls = new List<string>();
        await using var provider = BuildHost(NewDbFile(), calls);
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var dispatcher = provider.GetRequiredService<IDeferredDispatcher>();
        var key = await store.RecordAsync(
            new DeferredEnvelope<DeadLetterPing>(new DeadLetterPing("replay-me"), DateTimeOffset.UtcNow, null),
            new InvalidOperationException("original"),
            DateTimeOffset.UtcNow,
            TestContext!.CancellationToken);

        await ((IHostedService)dispatcher).StartAsync(TestContext!.CancellationToken);

        try {
            // Act
            var replayed = await dispatcher.ReplayAsync(key, TestContext!.CancellationToken);

            // Assert
            MSTestAssertions.That(() => replayed.Should().BeTrue());
            await SpinUntilAsync(() => Task.FromResult(calls.Count == 1), "replayed command");
            await SpinUntilAsync(async () => await store.GetAsync(key, TestContext!.CancellationToken).ConfigureAwait(false) is null, "entry removed");
            MSTestAssertions.That(() => calls.Should().Equal(["replay-me"]));
        }
        finally {
            await ((IHostedService)dispatcher).StopAsync(default).ConfigureAwait(false);
        }
    }

    [TestMethod]
    public async Task Prune_RemovesEntriesOutsideRetentionAsync() {
        // Arrange: one entry older than the one-hour retention.
        await using var provider = BuildHost(NewDbFile(), [], static options => options.Retention = TimeSpan.FromHours(1));
        var store = provider.GetRequiredService<IDeadLetterStore>();
        var now = DateTimeOffset.UtcNow;
        var key = await store.RecordAsync(
            new DeferredEnvelope<DeadLetterPing>(new DeadLetterPing("stale"), now, null),
            new InvalidOperationException("stale"),
            now.AddHours(-2),
            TestContext!.CancellationToken);

        // Act
        var removed = await store.PruneAsync(now, TestContext!.CancellationToken);

        // Assert
        MSTestAssertions.That(() => removed.Should().Be(1));
        MSTestAssertions.That(() => store.GetAsync(key, TestContext!.CancellationToken).GetAwaiter().GetResult().Should().BeNull());
    }

    private sealed record DeadLetterPing(string Name) : ICommand;

    private sealed class DeadLetterPingHandler(List<string> calls) : ICommandHandler<DeadLetterPing> {
        public Task<Unit> HandleAsync(DeadLetterPing command, CancellationToken cancellationToken) {
            calls.Add(command.Name);

            return Task.FromResult(Unit.Value);
        }
    }
}
