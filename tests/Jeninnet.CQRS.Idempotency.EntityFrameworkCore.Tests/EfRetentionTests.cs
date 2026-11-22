//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore.Tests;

/// <summary>
/// CQRS-64 retention: completed rows archive after their policy cutoff, dead letters wait for the
/// grace period, dry-run reports the same counts without touching storage, and a completed outbox
/// row is archived when retention is enabled.
/// </summary>
[TestClass]
public sealed class EfRetentionTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task Inbox_ArchivesOldCompletedRows_DryRunTouchesNothingAsync() {
        // Arrange
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var file = NewDbFile();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddCqrsEfInbox(db => db.UseSqlite($"Data Source={file}"));
        services.AddCqrsEfRetention(configure: options => {
            options.BatchSize = 10;
            options.DryRun = true;
            options.AddPolicy(RetentionStoreNames.Inbox, new RetentionPolicy {
                Retention = TimeSpan.FromDays(1),
            });
        });

        await using var provider = services.BuildServiceProvider();
        EnsureCreated(provider);
        var inbox = provider.GetRequiredService<IInboxStore>();
        var now = clock.GetUtcNow();

        await inbox.TryBeginAsync(new InboxMessage("old", now.AddDays(-10), now.AddDays(-9)), TestContext!.CancellationToken).ConfigureAwait(false);
        await inbox.CompleteAsync("old", null, null, now.AddDays(-10), TestContext.CancellationToken).ConfigureAwait(false);
        await inbox.TryBeginAsync(new InboxMessage("fresh", now, now.AddMinutes(5)), TestContext.CancellationToken).ConfigureAwait(false);
        await inbox.CompleteAsync("fresh", null, null, now, TestContext.CancellationToken).ConfigureAwait(false);

        var sweeper = provider.GetRequiredService<RetentionSweeper>();

        // Act: dry run reports the eligible batch but leaves both rows in place.
        var dryRun = await sweeper.SweepOnceAsync(TestContext.CancellationToken).ConfigureAwait(false);
        var dryResult = dryRun.Single(result => result.Store == RetentionStoreNames.Inbox);
        var oldAfterDryRun = await inbox.GetAsync("old", null, TestContext.CancellationToken).ConfigureAwait(false);
        var archiveAfterDryRun = await CountAsync<InboxArchiveRecord>(provider, TestContext.CancellationToken).ConfigureAwait(false);

        // Act: the real sweep archives only the old row.
        provider.GetRequiredService<IOptions<RetentionOptions>>().Value.DryRun = false;
        var actual = await sweeper.SweepOnceAsync(TestContext.CancellationToken).ConfigureAwait(false);
        var actualResult = actual.Single(result => result.Store == RetentionStoreNames.Inbox);
        var oldAfter = await inbox.GetAsync("old", null, TestContext.CancellationToken).ConfigureAwait(false);
        var freshAfter = await inbox.GetAsync("fresh", null, TestContext.CancellationToken).ConfigureAwait(false);
        var archiveAfter = await CountAsync<InboxArchiveRecord>(provider, TestContext.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => dryResult.Candidates.Should().Be(1));
        MSTestAssertions.That(() => dryResult.Archived.Should().Be(1));
        MSTestAssertions.That(() => dryResult.DryRun.Should().BeTrue());
        MSTestAssertions.That(() => oldAfterDryRun.Should().NotBeNull());
        MSTestAssertions.That(() => archiveAfterDryRun.Should().Be(0));
        MSTestAssertions.That(() => actualResult.Archived.Should().Be(1));
        MSTestAssertions.That(() => actualResult.DryRun.Should().BeFalse());
        MSTestAssertions.That(() => oldAfter.Should().BeNull());
        MSTestAssertions.That(() => freshAfter.Should().NotBeNull());
        MSTestAssertions.That(() => archiveAfter.Should().Be(1));
    }

    [TestMethod]
    public async Task DeadLetters_WaitForTheConfiguredGracePeriodBeforeArchivingAsync() {
        // Arrange
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var file = NewDbFile();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddCqrsEfDeadLetters(db => db.UseSqlite($"Data Source={file}"));
        services.AddCqrsEfRetention(configure: options => {
            options.DryRun = false;
            options.AddPolicy(RetentionStoreNames.DeadLetters, new RetentionPolicy {
                Retention = TimeSpan.FromDays(1),
                GracePeriod = TimeSpan.FromDays(1),
            });
        });

        await using var provider = services.BuildServiceProvider();
        EnsureCreated(provider);
        var deadLetters = provider.GetRequiredService<IDeadLetterStore>();
        var now = clock.GetUtcNow();

        await deadLetters.RecordAsync(
            new DeferredEnvelope<RetentionDeadLetter>(new RetentionDeadLetter("old"), now, null),
            new InvalidOperationException("old"),
            now.AddDays(-3),
            TestContext!.CancellationToken).ConfigureAwait(false);
        await deadLetters.RecordAsync(
            new DeferredEnvelope<RetentionDeadLetter>(new RetentionDeadLetter("recent"), now, null),
            new InvalidOperationException("recent"),
            now,
            TestContext.CancellationToken).ConfigureAwait(false);

        // Act
        var results = await provider.GetRequiredService<RetentionSweeper>()
            .SweepOnceAsync(TestContext.CancellationToken).ConfigureAwait(false);
        var result = results.Single(candidate => candidate.Store == RetentionStoreNames.DeadLetters);
        var remaining = await deadLetters.ListAsync(TestContext.CancellationToken).ConfigureAwait(false);
        var archiveCount = await CountAsync<DeadLetterArchiveRecord>(provider, TestContext.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => result.Candidates.Should().Be(1));
        MSTestAssertions.That(() => result.Archived.Should().Be(1));
        MSTestAssertions.That(() => remaining.Should().HaveCount(1));
        MSTestAssertions.That(() => remaining[0].Envelope.Command.Should().BeOfType<RetentionDeadLetter>());
        MSTestAssertions.That(() => ((RetentionDeadLetter)remaining[0].Envelope.Command).Name.Should().Be("recent"));
        MSTestAssertions.That(() => archiveCount.Should().Be(1));
    }

    [TestMethod]
    public async Task CompletedOutbox_IsArchivedWhenRetentionIsEnabledAsync() {
        // Arrange
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var file = NewDbFile();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddCqrsEfNotificationOutbox(db => db.UseSqlite($"Data Source={file}"));
        services.AddCqrsEfRetention(configure: options => {
            options.DryRun = false;
            options.AddPolicy(RetentionStoreNames.Outbox, new RetentionPolicy {
                Retention = TimeSpan.FromDays(1),
            });
        });

        await using var provider = services.BuildServiceProvider();
        EnsureCreated(provider);
        var outbox = provider.GetRequiredService<IOutboxStore>();

        clock.SetUtcNow(clock.GetUtcNow().AddDays(-10));
        var key = await outbox.StageAsync(
            new OutboxNotification<RetentionNotification>(new RetentionNotification("done"), clock.GetUtcNow()),
            TestContext!.CancellationToken).ConfigureAwait(false);
        await outbox.CompleteAsync(key, TestContext.CancellationToken).ConfigureAwait(false);
        clock.SetUtcNow(clock.GetUtcNow().AddDays(10));

        // Act
        var results = await provider.GetRequiredService<RetentionSweeper>()
            .SweepOnceAsync(TestContext.CancellationToken).ConfigureAwait(false);
        var result = results.Single(candidate => candidate.Store == RetentionStoreNames.Outbox);
        var listed = await outbox.ListAsync(TestContext.CancellationToken).ConfigureAwait(false);
        var archiveCount = await CountAsync<OutboxArchiveRecord>(provider, TestContext.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => result.Archived.Should().Be(1));
        MSTestAssertions.That(() => listed.Should().BeEmpty());
        MSTestAssertions.That(() => archiveCount.Should().Be(1));
    }

    [TestMethod]
    public async Task Jobs_ArchiveOnlyTerminalRowsPastRetentionAsync() {
        // Arrange
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero));
        var file = NewDbFile();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddCqrsEfJobs(db => db.UseSqlite($"Data Source={file}"));
        services.AddCqrsEfRetention(configure: options => {
            options.DryRun = false;
            options.AddPolicy(RetentionStoreNames.Jobs, new RetentionPolicy {
                Retention = TimeSpan.FromDays(1),
            });
        });

        await using var provider = services.BuildServiceProvider();
        EnsureCreated(provider);
        var now = clock.GetUtcNow();

        using(var scope = provider.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>().CreateDbContext();
            db.Jobs.Add(new JobRecordEntity {
                JobId = "job-old",
                JobType = "RetentionJob",
                Payload = [],
                Status = (int)JobStatus.Succeeded,
                Progress = 100,
                Attempts = 1,
                MaxAttempts = 1,
                CreatedAtUtc = now.AddDays(-10),
                UpdatedAtUtc = now.AddDays(-10),
                NextAttemptUtc = now.AddDays(-10),
                CompletedAtUtc = now.AddDays(-10),
            });
            db.Jobs.Add(new JobRecordEntity {
                JobId = "job-fresh",
                JobType = "RetentionJob",
                Payload = [],
                Status = (int)JobStatus.Succeeded,
                Progress = 100,
                Attempts = 1,
                MaxAttempts = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                NextAttemptUtc = now,
                CompletedAtUtc = now,
            });
            await db.SaveChangesAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        }

        // Act
        var results = await provider.GetRequiredService<RetentionSweeper>()
            .SweepOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        var result = results.Single(candidate => candidate.Store == RetentionStoreNames.Jobs);
        var oldJob = await provider.GetRequiredService<IJobStore>().GetAsync("job-old", TestContext!.CancellationToken).ConfigureAwait(false);
        var freshJob = await provider.GetRequiredService<IJobStore>().GetAsync("job-fresh", TestContext!.CancellationToken).ConfigureAwait(false);
        var archiveCount = await CountAsync<JobArchiveRecord>(provider, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => result.Archived.Should().Be(1));
        MSTestAssertions.That(() => oldJob.Should().BeNull());
        MSTestAssertions.That(() => freshJob.Should().NotBeNull());
        MSTestAssertions.That(() => archiveCount.Should().Be(1));
    }

    private static string NewDbFile() =>
        Path.Combine(Path.GetTempPath(), $"cqrs64-retention-{Guid.NewGuid():N}.db");

    private static void EnsureCreated(ServiceProvider provider) {
        using var scope = provider.CreateScope();
        using var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>().CreateDbContext();

        db.Database.EnsureCreated();
    }

    private static async Task<int> CountAsync<TEntity>(ServiceProvider provider, CancellationToken cancellationToken)
        where TEntity : class {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>().CreateDbContext();

        return await db.Set<TEntity>().CountAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record RetentionDeadLetter(string Name) : ICommand;

    private sealed record RetentionNotification(string Name) : INotification;

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        internal void SetUtcNow(DateTimeOffset now) => _now = now;
    }
}