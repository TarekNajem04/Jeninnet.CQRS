//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore.Tests;

/// <summary>
/// CQRS-59 durable job store: lease, progress, retry, cancellation, completion, and retention
/// round-trip through SQLite.
/// </summary>
[TestClass]
public sealed class EfJobStoreTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task EfJob_LeasesReportsProgressAndCompletesAsync() {
        // Arrange
        var file = NewDbFile();
        var services = new ServiceCollection();
        services.AddCqrsEfJobs(db => db.UseSqlite($"Data Source={file}"));
        await using var provider = services.BuildServiceProvider();
        EnsureCreated(provider);
        var store = provider.GetRequiredService<IJobStore>();
        var now = DateTimeOffset.UtcNow;
        var created = await store.CreateAsync(NewJob("job-1", now, maxAttempts: 2), TestContext!.CancellationToken).ConfigureAwait(false);

        // Act: lease and report progress.
        var leased = await store.LeaseDueAsync("worker-a", 10, now, TimeSpan.FromMinutes(1), TestContext!.CancellationToken).ConfigureAwait(false);
        var progressWritten = await store.ReportProgressAsync("job-1", "worker-a", 42, TestContext!.CancellationToken).ConfigureAwait(false);
        var running = await store.GetAsync("job-1", TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => store.Should().BeOfType<EfJobStore>());
        MSTestAssertions.That(() => created.Version.Should().Be(1));
        MSTestAssertions.That(() => leased.Should().HaveCount(1));
        MSTestAssertions.That(() => leased[0].Status.Should().Be(JobStatus.Running));
        MSTestAssertions.That(() => leased[0].Attempts.Should().Be(1));
        MSTestAssertions.That(() => leased[0].LeaseOwner.Should().Be("worker-a"));
        MSTestAssertions.That(() => progressWritten.Should().BeTrue());
        MSTestAssertions.That(() => running!.Progress.Should().Be(42));

        // Act: complete and verify terminal immutability.
        var completed = await store.CompleteAsync("job-1", "worker-a", JobStatus.Succeeded, 100, null, now.AddSeconds(1), TestContext!.CancellationToken).ConfigureAwait(false);
        var second = await store.CompleteAsync("job-1", "worker-a", JobStatus.Failed, 100, null, now.AddSeconds(2), TestContext!.CancellationToken).ConfigureAwait(false);
        var terminal = await store.GetAsync("job-1", TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        var terminalStatus = terminal!;
        MSTestAssertions.That(() => completed.Should().BeTrue());
        MSTestAssertions.That(() => second.Should().BeFalse());
        MSTestAssertions.That(() => terminalStatus.Status.Should().Be(JobStatus.Succeeded));
        MSTestAssertions.That(() => terminalStatus.Progress.Should().Be(100));
        MSTestAssertions.That(() => terminalStatus.CompletedAtUtc.HasValue.Should().BeTrue());
    }

    [TestMethod]
    public async Task EfJob_RetryCancelAndPruneAsync() {
        // Arrange
        var file = NewDbFile();
        var services = new ServiceCollection();
        services.AddCqrsEfJobs(db => db.UseSqlite($"Data Source={file}"));
        await using var provider = services.BuildServiceProvider();
        EnsureCreated(provider);
        var store = provider.GetRequiredService<IJobStore>();
        var now = DateTimeOffset.UtcNow;
        await store.CreateAsync(NewJob("job-retry", now, maxAttempts: 2), TestContext!.CancellationToken).ConfigureAwait(false);
        await store.LeaseDueAsync("worker-a", 10, now, TimeSpan.FromMinutes(1), TestContext!.CancellationToken).ConfigureAwait(false);

        // Act: retry with backoff.
        var retried = await store.RetryAsync(
            "job-retry",
            "worker-a",
            new InvalidOperationException("boom"),
            now.AddSeconds(5),
            TestContext!.CancellationToken).ConfigureAwait(false);
        var queued = await store.GetAsync("job-retry", TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        var queuedStatus = queued!;
        MSTestAssertions.That(() => retried.Should().BeTrue());
        MSTestAssertions.That(() => queuedStatus.Status.Should().Be(JobStatus.Queued));
        MSTestAssertions.That(() => queuedStatus.NextAttemptUtc.Should().Be(now.AddSeconds(5)));
        MSTestAssertions.That(() => queuedStatus.LastErrorMessage.Should().Be("boom"));

        // Act: lease again and cancel it cooperatively.
        await store.LeaseDueAsync("worker-b", 10, now.AddSeconds(5), TimeSpan.FromMinutes(1), TestContext!.CancellationToken).ConfigureAwait(false);
        var cancelRequested = await store.RequestCancellationAsync("job-retry", now.AddSeconds(6), TestContext!.CancellationToken).ConfigureAwait(false);
        var flag = await store.IsCancellationRequestedAsync("job-retry", TestContext!.CancellationToken).ConfigureAwait(false);
        await store.CompleteAsync("job-retry", "worker-b", JobStatus.Canceled, 0, null, now.AddSeconds(7), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => cancelRequested.Should().BeTrue());
        MSTestAssertions.That(() => flag.Should().BeTrue());

        // Arrange: an old terminal row and a queued row.
        await store.CreateAsync(NewJob("job-old", now.AddDays(-10), maxAttempts: 1), TestContext!.CancellationToken).ConfigureAwait(false);
        await store.LeaseDueAsync("worker-a", 10, now.AddDays(-10), TimeSpan.FromMinutes(1), TestContext!.CancellationToken).ConfigureAwait(false);
        await store.CompleteAsync("job-old", "worker-a", JobStatus.Succeeded, 100, null, now.AddDays(-10), TestContext!.CancellationToken).ConfigureAwait(false);
        await store.CreateAsync(NewJob("job-pending", now, maxAttempts: 1), TestContext!.CancellationToken).ConfigureAwait(false);

        // Act
        var pruned = await store.PruneAsync(now.AddDays(-7), TestContext!.CancellationToken).ConfigureAwait(false);
        var old = await store.GetAsync("job-old", TestContext!.CancellationToken).ConfigureAwait(false);
        var pending = await store.GetAsync("job-pending", TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => pruned.Should().Be(1));
        MSTestAssertions.That(() => old.Should().BeNull());
        MSTestAssertions.That(() => pending.Should().NotBeNull());
    }

    [TestMethod]
    public async Task EfJob_GuardsAndNonRunningRowsReturnFalseAsync() {
        // Arrange
        var file = NewDbFile();
        var services = new ServiceCollection();
        services.AddCqrsEfJobs(db => db.UseSqlite($"Data Source={file}"));
        await using var provider = services.BuildServiceProvider();
        EnsureCreated(provider);
        var store = provider.GetRequiredService<IJobStore>();
        var now = DateTimeOffset.UtcNow;
        var cancellationToken = TestContext!.CancellationToken;
        await store.CreateAsync(NewJob("job-guards", now, maxAttempts: 2), cancellationToken).ConfigureAwait(false);

        // Act + Assert: argument guards and duplicate keys.
        _ = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => store.LeaseDueAsync("worker", 0, now, TimeSpan.FromMinutes(1), cancellationToken)).ConfigureAwait(false);
        _ = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => store.LeaseDueAsync("worker", 1, now, TimeSpan.Zero, cancellationToken)).ConfigureAwait(false);
        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.CreateAsync(NewJob("job-guards", now, maxAttempts: 2), cancellationToken)).ConfigureAwait(false);
        _ = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => store.CompleteAsync("job-guards", "worker", JobStatus.Running, 0, null, now, cancellationToken)).ConfigureAwait(false);

        // Missing and queued rows never report progress, renew, complete, or retry.
        Assert.IsNull(await store.GetAsync("missing", cancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await store.ReportProgressAsync("missing", "worker", 10, cancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await store.ReportProgressAsync("job-guards", "worker", 10, cancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await store.RenewLeaseAsync("missing", "worker", TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await store.RenewLeaseAsync("job-guards", "worker", TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await store.RequestCancellationAsync("missing", now, cancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await store.IsCancellationRequestedAsync("missing", cancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await store.CompleteAsync("missing", "worker", JobStatus.Succeeded, 100, null, now, cancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await store.RetryAsync("missing", "worker", new InvalidOperationException("boom"), now, cancellationToken).ConfigureAwait(false));
        Assert.AreEqual(0, await store.PruneAsync(now.AddDays(-1), cancellationToken).ConfigureAwait(false));
        Assert.AreEqual(1L, await store.CountAsync(JobStatus.Queued, cancellationToken).ConfigureAwait(false));

        // A queued cancellation moves straight to Canceled; repeating it is harmless.
        Assert.IsTrue(await store.RequestCancellationAsync("job-guards", now, cancellationToken).ConfigureAwait(false));
        var canceled = await store.GetAsync("job-guards", cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(JobStatus.Canceled, canceled!.Status);
        Assert.IsTrue(await store.RequestCancellationAsync("job-guards", now, cancellationToken).ConfigureAwait(false));

        // Lease a running row and exercise the owner checks.
        await store.CreateAsync(NewJob("job-running", now, maxAttempts: 1), cancellationToken).ConfigureAwait(false);
        var leased = await store.LeaseDueAsync("worker", 10, now, TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(1, leased.Count);
        Assert.IsTrue(await store.ReportProgressAsync("job-running", "worker", 200, cancellationToken).ConfigureAwait(false));
        var progressed = await store.GetAsync("job-running", cancellationToken).ConfigureAwait(false);
        Assert.AreEqual(100, progressed!.Progress);
        Assert.IsFalse(await store.ReportProgressAsync("job-running", "other", 5, cancellationToken).ConfigureAwait(false));
        Assert.IsTrue(await store.RenewLeaseAsync("job-running", "worker", TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await store.RenewLeaseAsync("job-running", "other", TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await store.CompleteAsync("job-running", "other", JobStatus.Succeeded, 100, null, now, cancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await store.RetryAsync("job-running", "other", new InvalidOperationException("boom"), now, cancellationToken).ConfigureAwait(false));

        // Cooperative cancellation while running, then terminal completion and pruning.
        Assert.IsTrue(await store.RequestCancellationAsync("job-running", now, cancellationToken).ConfigureAwait(false));
        Assert.IsTrue(await store.IsCancellationRequestedAsync("job-running", cancellationToken).ConfigureAwait(false));
        Assert.IsTrue(await store.CompleteAsync("job-running", "worker", JobStatus.Canceled, 50, new InvalidOperationException("stop"), now, cancellationToken).ConfigureAwait(false));
        Assert.AreEqual(2, await store.PruneAsync(now.AddSeconds(1), cancellationToken).ConfigureAwait(false));
        Assert.AreEqual(0L, await store.CountAsync(JobStatus.Canceled, cancellationToken).ConfigureAwait(false));
    }

    private static JobRecord NewJob(string jobId, DateTimeOffset now, int maxAttempts) =>
        new() {
            JobId = jobId,
            JobType = "test.job",
            Payload = [],
            Status = JobStatus.Queued,
            Progress = 0,
            Attempts = 0,
            MaxAttempts = maxAttempts,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            NextAttemptUtc = now,
        };

    private static void EnsureCreated(IServiceProvider provider) {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
    }

    private static string NewDbFile() =>
        Path.Combine(Path.GetTempPath(), $"cqrs59-jobs-{Guid.NewGuid():N}.db");
}
