//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// The three background pruners: inbox, jobs, and request inbox. Each must perform one prune pass
/// on its configured interval and stop cleanly when the host shuts down.
/// </summary>
[TestClass]
public sealed class PruningServiceTests {
    [TestMethod]
    public async Task InboxPruningService_PrunesOnIntervalAsync() {
        var store = new FakeInboxStore();
        using var metrics = new InboxMetrics();
        var options = Options.Create(new InboxOptions {
            PruneInterval = TimeSpan.FromMilliseconds(10),
            Retention = TimeSpan.FromHours(1),
            PendingTimeout = TimeSpan.FromMinutes(1),
        });
        var service = new InboxPruningService(store, options, TimeProvider.System, metrics);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await service.StartAsync(cancellation.Token).ConfigureAwait(false);

        try {
            await store.Pruned.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellation.Token).ConfigureAwait(false);
        }
        finally {
            await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        Assert.IsTrue(store.PruneCalls >= 1);
    }

    [TestMethod]
    public async Task JobPruningService_PrunesOnIntervalAsync() {
        var store = new FakeJobStore();
        var options = Options.Create(new JobOptions {
            PruneInterval = TimeSpan.FromMilliseconds(10),
        });
        var service = new JobPruningService(store, options, TimeProvider.System);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await service.StartAsync(cancellation.Token).ConfigureAwait(false);

        try {
            await store.Pruned.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellation.Token).ConfigureAwait(false);
        }
        finally {
            await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        Assert.IsTrue(store.PruneCalls >= 1);
    }

    [TestMethod]
    public async Task RequestInboxPruningService_RemovesExpiredRowsOnIntervalAsync() {
        var inbox = new FakeRequestInbox();
        var options = Options.Create(new RequestResponseOptions {
            InboxPruneInterval = TimeSpan.FromMilliseconds(10),
        });
        var service = new RequestInboxPruningService(inbox, options, TimeProvider.System);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await service.StartAsync(cancellation.Token).ConfigureAwait(false);

        try {
            await inbox.Pruned.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellation.Token).ConfigureAwait(false);
        }
        finally {
            await service.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        Assert.IsTrue(inbox.PruneCalls >= 1);
    }

    private sealed class FakeInboxStore : IInboxStore {
        public TaskCompletionSource Pruned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int PruneCalls { get; private set; }

        public Task<bool> TryBeginAsync(InboxMessage message, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CompleteAsync(string messageId, string? tenantId, string? resultHash, DateTimeOffset processedAtUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AbandonAsync(string messageId, string? tenantId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<InboxEntry?> GetAsync(string messageId, string? tenantId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsProcessedAsync(string messageId, string? tenantId = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> PruneAsync(DateTimeOffset processedBeforeUtc, DateTimeOffset pendingBeforeUtc, CancellationToken cancellationToken = default) {
            PruneCalls++;
            Pruned.TrySetResult();
            return Task.FromResult(0);
        }
    }

    private sealed class FakeJobStore : IJobStore {
        public TaskCompletionSource Pruned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int PruneCalls { get; private set; }

        public Task<JobRecord> CreateAsync(JobRecord record, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<JobRecord?> GetAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<JobRecord>> LeaseDueAsync(string owner, int batchSize, DateTimeOffset now, TimeSpan leaseDuration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ReportProgressAsync(string jobId, string owner, int progress, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RenewLeaseAsync(string jobId, string owner, TimeSpan leaseDuration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RequestCancellationAsync(string jobId, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> IsCancellationRequestedAsync(string jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> CompleteAsync(string jobId, string owner, JobStatus status, int progress, Exception? error, DateTimeOffset now, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RetryAsync(string jobId, string owner, Exception error, DateTimeOffset nextAttemptUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<long> CountAsync(JobStatus status, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> PruneAsync(DateTimeOffset completedBeforeUtc, CancellationToken cancellationToken = default) {
            PruneCalls++;
            Pruned.TrySetResult();
            return Task.FromResult(0);
        }
    }

    private sealed class FakeRequestInbox : IRequestInbox {
        public TaskCompletionSource Pruned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int PruneCalls { get; private set; }

        public Task AddAsync(RequestInboxEntry entry, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveAsync(string requestId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RequestInboxEntry>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<int> RemoveExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default) {
            PruneCalls++;
            Pruned.TrySetResult();
            return Task.FromResult(0);
        }
    }
}
