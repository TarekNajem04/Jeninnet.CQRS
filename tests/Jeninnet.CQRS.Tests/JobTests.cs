//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// CQRS-59 job consumers: submit/run/status, progress, retry/backoff, terminal dead-lettering,
/// cooperative cancellation, queued cancellation, and retention pruning.
/// </summary>
[TestClass]
public sealed class JobTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task SubmitRun_ReportsProgress_AndSucceedsAsync() {
        // Arrange
        var probe = new JobProbe();
        await using var host = BuildHost(
            services => {
                services.AddSingleton(probe);
                services.AddJobHandler<ProgressJob, ProgressJobHandler>();
            });

        // Act
        var jobId = await host.Queue.SubmitAsync(new ProgressJob("daily"), TestContext!.CancellationToken).ConfigureAwait(false);
        var run = host.Worker.RunOnceAsync(TestContext!.CancellationToken);
        await probe.ProgressReported.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext!.CancellationToken).ConfigureAwait(false);
        var running = await host.Client.GetStatusAsync(jobId, TestContext!.CancellationToken).ConfigureAwait(false);
        probe.Release.TrySetResult();
        var result = await run.ConfigureAwait(false);
        var completed = await host.Client.GetStatusAsync(jobId, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => running.Should().NotBeNull());
        var runningStatus = running!;
        var completedStatus = completed!;
        MSTestAssertions.That(() => runningStatus.Status.Should().Be(JobStatus.Running));
        MSTestAssertions.That(() => runningStatus.Progress.Should().Be(50));
        MSTestAssertions.That(() => runningStatus.Attempts.Should().Be(1));
        MSTestAssertions.That(() => result.Succeeded.Should().Be(1));
        MSTestAssertions.That(() => completed.Should().NotBeNull());
        MSTestAssertions.That(() => completedStatus.Status.Should().Be(JobStatus.Succeeded));
        MSTestAssertions.That(() => completedStatus.Progress.Should().Be(100));
        MSTestAssertions.That(() => completedStatus.CompletedAtUtc.HasValue.Should().BeTrue());
    }

    [TestMethod]
    public async Task Failure_RetriesPerPolicy_ThenFailsAndDeadLettersAsync() {
        // Arrange
        var probe = new JobProbe();
        await using var host = BuildHost(
            services => {
                services.AddSingleton(probe);
                services.AddJobHandler<FailingJob, AlwaysFailHandler>();
            },
            options => {
                options.MaxAttempts = 3;
                options.InitialRetryDelay = TimeSpan.FromSeconds(1);
                options.BackoffMultiplier = 2;
                options.MaxRetryDelay = TimeSpan.FromSeconds(5);
            });

        // Act: first attempt.
        var jobId = await host.Queue.SubmitAsync(new FailingJob("boom"), TestContext!.CancellationToken).ConfigureAwait(false);
        var first = await host.Worker.RunOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        var afterFirst = await host.Client.GetStatusAsync(jobId, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert: requeued with the first backoff.
        var firstStatus = afterFirst!;
        MSTestAssertions.That(() => first.Retried.Should().Be(1));
        MSTestAssertions.That(() => firstStatus.Status.Should().Be(JobStatus.Queued));
        MSTestAssertions.That(() => firstStatus.Attempts.Should().Be(1));
        MSTestAssertions.That(() => firstStatus.NextAttemptUtc.Should().Be(host.Clock.GetUtcNow() + TimeSpan.FromSeconds(1)));
        MSTestAssertions.That(() => firstStatus.LastErrorMessage.Should().Be("boom"));

        // Act: second attempt after the first delay.
        host.Clock.Advance(TimeSpan.FromSeconds(1));
        var second = await host.Worker.RunOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        var afterSecond = await host.Client.GetStatusAsync(jobId, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        var secondStatus = afterSecond!;
        MSTestAssertions.That(() => second.Retried.Should().Be(1));
        MSTestAssertions.That(() => secondStatus.Attempts.Should().Be(2));
        MSTestAssertions.That(() => secondStatus.NextAttemptUtc.Should().Be(host.Clock.GetUtcNow() + TimeSpan.FromSeconds(2)));

        // Act: third attempt exhausts the budget.
        host.Clock.Advance(TimeSpan.FromSeconds(2));
        var third = await host.Worker.RunOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        var failed = await host.Client.GetStatusAsync(jobId, TestContext!.CancellationToken).ConfigureAwait(false);
        var deadLetters = await host.DeadLetters.ListAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        var failedStatus = failed!;
        MSTestAssertions.That(() => third.Failed.Should().Be(1));
        MSTestAssertions.That(() => failedStatus.Status.Should().Be(JobStatus.Failed));
        MSTestAssertions.That(() => failedStatus.Attempts.Should().Be(3));
        MSTestAssertions.That(() => failedStatus.LastErrorType.Should().Contain(nameof(InvalidOperationException)));
        MSTestAssertions.That(() => failedStatus.LastErrorMessage.Should().Be("boom"));
        MSTestAssertions.That(() => deadLetters.Should().HaveCount(1));
        MSTestAssertions.That(() => deadLetters[0].LastErrorMessage.Should().Contain("boom"));
        MSTestAssertions.That(() => (deadLetters[0].Envelope.Command as JobDeadLetterInfo)!.JobId.Should().Be(jobId));
    }

    [TestMethod]
    public async Task Cancel_StopsARunningJobCooperatively_AndIsIdempotentAsync() {
        // Arrange
        var probe = new JobProbe();
        await using var host = BuildHost(
            services => {
                services.AddSingleton(probe);
                services.AddJobHandler<CancellableJob, CancellableJobHandler>();
            },
            options => options.CancellationPollInterval = TimeSpan.FromMilliseconds(10));

        var jobId = await host.Queue.SubmitAsync(new CancellableJob("cancel-me"), TestContext!.CancellationToken).ConfigureAwait(false);
        var run = host.Worker.RunOnceAsync(TestContext!.CancellationToken);
        await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext!.CancellationToken).ConfigureAwait(false);

        // Act
        var first = await host.Queue.CancelAsync(jobId, TestContext!.CancellationToken).ConfigureAwait(false);
        var result = await run.ConfigureAwait(false);
        var canceled = await host.Client.GetStatusAsync(jobId, TestContext!.CancellationToken).ConfigureAwait(false);
        var second = await host.Queue.CancelAsync(jobId, TestContext!.CancellationToken).ConfigureAwait(false);
        var afterSecond = await host.Client.GetStatusAsync(jobId, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        var canceledStatus = canceled!;
        var afterSecondStatus = afterSecond!;
        MSTestAssertions.That(() => first.Should().BeTrue());
        MSTestAssertions.That(() => second.Should().BeTrue());
        MSTestAssertions.That(() => result.Canceled.Should().Be(1));
        MSTestAssertions.That(() => canceledStatus.Status.Should().Be(JobStatus.Canceled));
        MSTestAssertions.That(() => canceledStatus.CancellationRequested.Should().BeTrue());
        MSTestAssertions.That(() => afterSecondStatus.Status.Should().Be(JobStatus.Canceled));
    }

    [TestMethod]
    public async Task Cancel_AQueuedJob_MarksItCanceledWithoutRunningAsync() {
        // Arrange
        var probe = new JobProbe();
        await using var host = BuildHost(services => {
            services.AddSingleton(probe);
            services.AddJobHandler<CancellableJob, CountingCancellableHandler>();
        });

        var jobId = await host.Queue.SubmitAsync(new CancellableJob("queued"), TestContext!.CancellationToken).ConfigureAwait(false);

        // Act
        var canceled = await host.Queue.CancelAsync(jobId, TestContext!.CancellationToken).ConfigureAwait(false);
        var result = await host.Worker.RunOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        var status = await host.Client.GetStatusAsync(jobId, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        var queuedStatus = status!;
        MSTestAssertions.That(() => canceled.Should().BeTrue());
        MSTestAssertions.That(() => result.Leased.Should().Be(0));
        MSTestAssertions.That(() => probe.Started.Task.IsCompleted.Should().BeFalse());
        MSTestAssertions.That(() => queuedStatus.Status.Should().Be(JobStatus.Canceled));
    }

    [TestMethod]
    public async Task Retention_PrunesTerminalJobsOnlyAsync() {
        // Arrange
        await using var host = BuildHost(services => {
            services.AddJobHandler<ProgressJob, NoopJobHandler>();
        });

        var finishedId = await host.Queue.SubmitAsync(new ProgressJob("finished"), TestContext!.CancellationToken).ConfigureAwait(false);
        await host.Worker.RunOnceAsync(TestContext!.CancellationToken).ConfigureAwait(false);
        var pendingId = await host.Queue.SubmitAsync(new ProgressJob("pending"), TestContext!.CancellationToken).ConfigureAwait(false);
        host.Clock.Advance(TimeSpan.FromDays(10));

        // Act
        var pruned = await host.Store.PruneAsync(host.Clock.GetUtcNow() - TimeSpan.FromDays(7), TestContext!.CancellationToken).ConfigureAwait(false);
        var finished = await host.Client.GetStatusAsync(finishedId, TestContext!.CancellationToken).ConfigureAwait(false);
        var pending = await host.Client.GetStatusAsync(pendingId, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        var pendingStatus = pending!;
        MSTestAssertions.That(() => pruned.Should().Be(1));
        MSTestAssertions.That(() => finished.Should().BeNull());
        MSTestAssertions.That(() => pending.Should().NotBeNull());
        MSTestAssertions.That(() => pendingStatus.Status.Should().Be(JobStatus.Queued));
    }

    [TestMethod]
    public async Task InMemoryStore_GuardsDuplicatesAndMissingRowsAsync() {
        // Arrange
        await using var host = BuildHost(static _ => { });
        var store = host.Store;
        var cancellationToken = TestContext!.CancellationToken;
        var now = host.Clock.GetUtcNow();
        var record = new JobRecord {
            JobId = "in-memory-1",
            JobType = "test.job",
            Payload = [],
            Status = JobStatus.Queued,
            Progress = 0,
            Attempts = 0,
            MaxAttempts = 1,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            NextAttemptUtc = now,
        };

        // Act + Assert
        Assert.IsFalse(string.IsNullOrWhiteSpace(host.Worker.Owner));
        _ = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => store.LeaseDueAsync("worker", 0, now, TimeSpan.FromMinutes(1), cancellationToken)).ConfigureAwait(false);
        _ = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => store.LeaseDueAsync("worker", 1, now, TimeSpan.Zero, cancellationToken)).ConfigureAwait(false);

        _ = await store.CreateAsync(record, cancellationToken).ConfigureAwait(false);
        _ = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => store.CreateAsync(record, cancellationToken)).ConfigureAwait(false);

        Assert.IsNull(await store.GetAsync("missing", cancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await store.RenewLeaseAsync("missing", "worker", TimeSpan.FromMinutes(1), cancellationToken).ConfigureAwait(false));
        _ = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => store.RenewLeaseAsync(record.JobId, "worker", TimeSpan.Zero, cancellationToken)).ConfigureAwait(false);
        Assert.IsFalse(await store.CompleteAsync("missing", "worker", JobStatus.Succeeded, 100, null, now, cancellationToken).ConfigureAwait(false));
        _ = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => store.CompleteAsync(record.JobId, "worker", JobStatus.Running, 0, null, now, cancellationToken)).ConfigureAwait(false);
        Assert.IsFalse(await store.RetryAsync("missing", "worker", new InvalidOperationException("boom"), now, cancellationToken).ConfigureAwait(false));
        Assert.IsFalse(await store.RequestCancellationAsync("missing", now, cancellationToken).ConfigureAwait(false));
        Assert.AreEqual(0L, await store.CountAsync(JobStatus.Canceled, cancellationToken).ConfigureAwait(false));
        Assert.AreEqual(1L, await store.CountAsync(JobStatus.Queued, cancellationToken).ConfigureAwait(false));
    }

    private static JobHost BuildHost(Action<IServiceCollection> configureServices, Action<JobOptions>? configureOptions = null) {
        var clock = new JobTestClock();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(clock);
        services.AddCqrsJobs(options => {
            options.OwnerId = "test-worker";
            options.PollInterval = TimeSpan.FromMilliseconds(10);
            configureOptions?.Invoke(options);
        });
        configureServices(services);

        var provider = services.BuildServiceProvider();

        return new JobHost(
            provider,
            clock,
            provider.GetRequiredService<IJobQueue>(),
            provider.GetRequiredService<IJobClient>(),
            provider.GetRequiredService<JobWorker>(),
            provider.GetRequiredService<IJobStore>(),
            provider.GetRequiredService<IDeadLetterStore>());
    }

    private sealed class JobHost(
        ServiceProvider provider,
        JobTestClock clock,
        IJobQueue queue,
        IJobClient client,
        JobWorker worker,
        IJobStore store,
        IDeadLetterStore deadLetters) : IAsyncDisposable {
        internal ServiceProvider Provider { get; } = provider;

        internal JobTestClock Clock { get; } = clock;

        internal IJobQueue Queue { get; } = queue;

        internal IJobClient Client { get; } = client;

        internal JobWorker Worker { get; } = worker;

        internal IJobStore Store { get; } = store;

        internal IDeadLetterStore DeadLetters { get; } = deadLetters;

        public ValueTask DisposeAsync() => Provider.DisposeAsync();
    }

    private sealed class JobTestClock : TimeProvider {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

        internal void Advance(TimeSpan by) => _now += by;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
            throw new NotSupportedException("The job tests only read the clock.");
    }

    public sealed class JobProbe {
        internal TaskCompletionSource ProgressReported { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed record ProgressJob(string Name);

    public sealed record FailingJob(string Name);

    public sealed record CancellableJob(string Name);

    public sealed class ProgressJobHandler(JobProbe probe) : IJobHandler<ProgressJob> {
        private readonly JobProbe _probe = probe ?? throw new ArgumentNullException(nameof(probe));

        public async Task ExecuteAsync(ProgressJob job, IJobProgress progress, CancellationToken cancellationToken) {
            await progress.ReportAsync(50, cancellationToken).ConfigureAwait(false);
            _probe.ProgressReported.TrySetResult();
            await _probe.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public sealed class NoopJobHandler : IJobHandler<ProgressJob> {
        public Task ExecuteAsync(ProgressJob job, IJobProgress progress, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class AlwaysFailHandler : IJobHandler<FailingJob> {
        public Task ExecuteAsync(FailingJob job, IJobProgress progress, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException("boom"));
    }

    public sealed class CancellableJobHandler(JobProbe probe) : IJobHandler<CancellableJob> {
        private readonly JobProbe _probe = probe ?? throw new ArgumentNullException(nameof(probe));

        public async Task ExecuteAsync(CancellableJob job, IJobProgress progress, CancellationToken cancellationToken) {
            _probe.Started.TrySetResult();

            try {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch(OperationCanceledException) {
                throw;
            }
        }
    }

    public sealed class CountingCancellableHandler(JobProbe probe) : IJobHandler<CancellableJob> {
        private readonly JobProbe _probe = probe ?? throw new ArgumentNullException(nameof(probe));

        public Task ExecuteAsync(CancellableJob job, IJobProgress progress, CancellationToken cancellationToken) {
            _probe.Started.TrySetResult();

            return Task.CompletedTask;
        }
    }
}