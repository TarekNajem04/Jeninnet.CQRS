//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.EntityFrameworkCore.Tests;

/// <summary>
/// CQRS-58 durable routing slips: progress survives a simulated crash after step two, the resumed
/// host continues at step three, and the EF store rejects a stale version.
/// </summary>
[TestClass]
public sealed class RoutingSlipEfTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task CrashAfterSecondStep_ResumesAtThirdAsync() {
        // Arrange
        var file = NewDbFile();
        var log = new List<string>();
        var gate = new SlipGate();
        var cancellation = new CancellationTokenSource();

        await using(var first = BuildHost(file, gate, log)) {
            EnsureCreated(first);
            var executor = first.GetRequiredService<RoutingSlipExecutor>();
            var running = executor.ExecuteAsync(new ResumeArguments(), "slip-1", cancellation.Token);

            await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext!.CancellationToken).ConfigureAwait(false);
            cancellation.Cancel();
            var thrown = await CaptureAsync(() => running).ConfigureAwait(false);
            var persisted = await first.GetRequiredService<IRoutingSlipStore>()
                .LoadAsync("slip-1", TestContext!.CancellationToken)
                .ConfigureAwait(false);

            // Assert: the shutdown stopped at step three without compensating.
            MSTestAssertions.That(() => thrown.Should().BeAssignableTo<OperationCanceledException>());
            MSTestAssertions.That(() => persisted.Should().NotBeNull());
            var stored = persisted!;
            MSTestAssertions.That(() => stored.Status.Should().Be(RoutingSlipStatus.Running));
            MSTestAssertions.That(() => stored.NextStepIndex.Should().Be(2));
            MSTestAssertions.That(() => stored.CompletedSteps.Should().Equal(["reserve", "charge"]));
        }

        cancellation.Dispose();

        // Act: a fresh host resumes the same file.
        await using(var second = BuildHost(file, gate, log)) {
            EnsureCreated(second);
            var result = await second.GetRequiredService<RoutingSlipExecutor>()
                .ResumeAsync("slip-1", TestContext!.CancellationToken)
                .ConfigureAwait(false);
            var persisted = await second.GetRequiredService<IRoutingSlipStore>()
                .LoadAsync("slip-1", TestContext!.CancellationToken)
                .ConfigureAwait(false);

            // Assert
            MSTestAssertions.That(() => result.Status.Should().Be(RoutingSlipStatus.Completed));
            MSTestAssertions.That(() => result.CompletedStepCount.Should().Be(3));
            MSTestAssertions.That(() => log.Should().Equal(["reserve", "charge", "ship"]));
            var stored = persisted!;
            MSTestAssertions.That(() => stored.Status.Should().Be(RoutingSlipStatus.Completed));
            MSTestAssertions.That(() => stored.Version.Should().BeGreaterThan(1));
        }
    }

    [TestMethod]
    public async Task EfStore_SavesProgress_AndRejectsAStaleVersionAsync() {
        // Arrange
        var file = NewDbFile();
        var services = new ServiceCollection();
        services.AddCqrsEfRoutingSlips(db => db.UseSqlite($"Data Source={file}"));
        await using var provider = services.BuildServiceProvider();
        EnsureCreated(provider);
        var store = provider.GetRequiredService<IRoutingSlipStore>();
        var now = DateTimeOffset.UtcNow;

        var created = await store.CreateAsync(new RoutingSlipRecord {
            SlipId = "slip-1",
            DefinitionName = "store",
            ArgumentsType = typeof(object).AssemblyQualifiedName ?? nameof(Object),
            ArgumentsPayload = [],
            Status = RoutingSlipStatus.Running,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        }, TestContext!.CancellationToken).ConfigureAwait(false);

        // Act
        var saved = await store.SaveAsync(created with {
            Status = RoutingSlipStatus.Compensating,
            NextStepIndex = 1,
            CompletedSteps = ["reserve"],
            UpdatedAtUtc = now.AddSeconds(1),
        }, created.Version, TestContext!.CancellationToken).ConfigureAwait(false);
        var stale = await CaptureAsync(() => store.SaveAsync(created with {
            Status = RoutingSlipStatus.Completed,
        }, created.Version, TestContext!.CancellationToken)).ConfigureAwait(false);
        var loaded = await store.LoadAsync("slip-1", TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => created.Version.Should().Be(1));
        MSTestAssertions.That(() => saved.Version.Should().Be(2));
        MSTestAssertions.That(() => stale.Should().BeOfType<RoutingSlipConcurrencyException>());
        MSTestAssertions.That(() => loaded.Should().NotBeNull());
        var stored = loaded!;
        MSTestAssertions.That(() => stored.Status.Should().Be(RoutingSlipStatus.Compensating));
        MSTestAssertions.That(() => stored.CompletedSteps.Should().Equal(["reserve"]));
        MSTestAssertions.That(() => stored.Version.Should().Be(2));
    }

    [TestMethod]
    public async Task EfStore_ListsCountsDeletesAndValidatesAsync() {
        // Arrange
        var file = NewDbFile();
        var services = new ServiceCollection();
        services.AddCqrsEfRoutingSlips(db => db.UseSqlite($"Data Source={file}"));
        await using var provider = services.BuildServiceProvider();
        EnsureCreated(provider);
        var store = provider.GetRequiredService<IRoutingSlipStore>();
        var now = DateTimeOffset.UtcNow;
        var cancellationToken = TestContext!.CancellationToken;
        var first = NewRecord("slip-1", "store", RoutingSlipStatus.Running, now);

        // Act + Assert: the negative-version guard and a save against a missing row.
        _ = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
            () => store.SaveAsync(first, -1, cancellationToken)).ConfigureAwait(false);
        _ = await Assert.ThrowsExactlyAsync<RoutingSlipConcurrencyException>(
            () => store.SaveAsync(first, 0, cancellationToken)).ConfigureAwait(false);

        _ = await store.CreateAsync(first, cancellationToken).ConfigureAwait(false);
        _ = await store.CreateAsync(NewRecord("slip-2", "other", RoutingSlipStatus.Completed, now.AddMinutes(1)), cancellationToken).ConfigureAwait(false);

        var all = await store.ListAsync(null, cancellationToken).ConfigureAwait(false);
        var filtered = await store.ListAsync("store", cancellationToken).ConfigureAwait(false);
        var running = await store.CountAsync(RoutingSlipStatus.Running, cancellationToken).ConfigureAwait(false);
        var missing = await store.LoadAsync("missing", cancellationToken).ConfigureAwait(false);
        await store.DeleteAsync("slip-1", cancellationToken).ConfigureAwait(false);
        var deleted = await store.LoadAsync("slip-1", cancellationToken).ConfigureAwait(false);
        var afterDelete = await store.CountAsync(RoutingSlipStatus.Running, cancellationToken).ConfigureAwait(false);

        // Assert
        Assert.AreEqual(2, all.Count);
        Assert.AreEqual(1, filtered.Count);
        Assert.AreEqual("slip-1", filtered[0].SlipId);
        Assert.AreEqual(1L, running);
        Assert.IsNull(missing);
        Assert.IsNull(deleted);
        Assert.AreEqual(0L, afterDelete);
    }

    private static RoutingSlipRecord NewRecord(string slipId, string definition, RoutingSlipStatus status, DateTimeOffset now) =>
        new() {
            SlipId = slipId,
            DefinitionName = definition,
            ArgumentsType = typeof(object).AssemblyQualifiedName ?? nameof(Object),
            ArgumentsPayload = [],
            Status = status,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };

    private static ServiceProvider BuildHost(string file, SlipGate gate, List<string> log) {
        var services = new ServiceCollection();
        services.AddSingleton(gate);
        services.AddSingleton(log);
        services.AddRoutingSlip<ResumeArguments>(
            slip => slip
                .AddStep<ReserveActivity>("reserve")
                .AddStep<ChargeActivity>("charge")
                .AddStep<ShipActivity>("ship"),
            "resume");
        services.AddCqrsEfRoutingSlips(db => db.UseSqlite($"Data Source={file}"));

        return services.BuildServiceProvider();
    }

    private static void EnsureCreated(IServiceProvider provider) {
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<SagaDbContext>>().CreateDbContext();
        db.Database.EnsureCreated();
    }

    private static string NewDbFile() =>
        Path.Combine(Path.GetTempPath(), $"cqrs58-routingslip-{Guid.NewGuid():N}.db");

    private static async Task<Exception?> CaptureAsync(Func<Task> action) {
        try {
            await action().ConfigureAwait(false);

            return null;
        }
        catch(Exception exception) {
            return exception;
        }
    }

    public sealed class SlipGate {
        private int _shipCalls;

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ShipAsync(CancellationToken cancellationToken) {
            if(Interlocked.Increment(ref _shipCalls) is 1) {
                Started.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public sealed class ResumeArguments {
        public string? Reservation { get; set; }
    }

    public sealed class ReserveActivity(List<string> log) : IActivity<ResumeArguments> {
        private readonly List<string> _log = log ?? throw new ArgumentNullException(nameof(log));

        public Task ExecuteAsync(ResumeArguments arguments, CancellationToken cancellationToken) {
            _log.Add("reserve");
            arguments.Reservation = "r-1";

            return Task.CompletedTask;
        }

        public Task CompensateAsync(ResumeArguments arguments, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class ChargeActivity(List<string> log) : IActivity<ResumeArguments> {
        private readonly List<string> _log = log ?? throw new ArgumentNullException(nameof(log));

        public Task ExecuteAsync(ResumeArguments arguments, CancellationToken cancellationToken) {
            _log.Add("charge");

            return Task.CompletedTask;
        }

        public Task CompensateAsync(ResumeArguments arguments, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    public sealed class ShipActivity(List<string> log, SlipGate gate) : IActivity<ResumeArguments> {
        private readonly List<string> _log = log ?? throw new ArgumentNullException(nameof(log));
        private readonly SlipGate _gate = gate ?? throw new ArgumentNullException(nameof(gate));

        public async Task ExecuteAsync(ResumeArguments arguments, CancellationToken cancellationToken) {
            await _gate.ShipAsync(cancellationToken).ConfigureAwait(false);
            _log.Add("ship");
        }

        public Task CompensateAsync(ResumeArguments arguments, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}