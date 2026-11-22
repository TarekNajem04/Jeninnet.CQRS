//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>Mutation-characterization tests for the default in-memory idempotency store.</summary>
[TestClass]
public sealed class DefaultIdempotencyStoreTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task Get_MissingKey_ReturnsDefaultAsync() {
        // Arrange
        var store = BuildStore(out _);

        // Act
        var value = await store.GetAsync<string>("missing", TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => value.Should().BeNull());
    }

    [TestMethod]
    public async Task Store_ThenGet_RequiresTheMatchingResponseTypeAsync() {
        // Arrange
        var store = BuildStore(out _);

        // Act
        await store.StoreAsync("key", "value", TestContext!.CancellationToken).ConfigureAwait(false);
        var matching = await store.GetAsync<string>("key", TestContext!.CancellationToken).ConfigureAwait(false);
        var mismatched = await store.GetAsync<int>("key", TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => matching.Should().Be("value"));
        MSTestAssertions.That(() => mismatched.Should().Be(0));
    }

    [TestMethod]
    public async Task TryAcquire_ZeroTimeToLive_ThrowsWithParameterNameAsync() {
        // Arrange
        var store = BuildStore(out _);

        // Act
        var thrown = await CaptureAsync(() => store.TryAcquireAsync<string>("key", "owner", TimeSpan.Zero, TestContext!.CancellationToken)).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<ArgumentOutOfRangeException>());
        MSTestAssertions.That(() => ((ArgumentOutOfRangeException)thrown!).ParamName.Should().Be("timeToLive"));
        MSTestAssertions.That(() => thrown!.Message.Should().Contain("time-to-live"));
    }

    [TestMethod]
    public async Task TryAcquire_AtTheExactExpiry_IsFreeAsync() {
        // Arrange
        var store = BuildStore(out var clock);
        var ttl = TimeSpan.FromSeconds(10);
        _ = await store.TryAcquireAsync<string>("key", "first", ttl, TestContext!.CancellationToken).ConfigureAwait(false);

        // Act
        clock.Advance(ttl);
        var second = await store.TryAcquireAsync<string>("key", "second", ttl, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => second.State.Should().Be(IdempotencyLeaseState.Acquired));
        MSTestAssertions.That(() => second.Owner.Should().Be("second"));
    }

    [TestMethod]
    public async Task Renew_MissingWrongOwnerAndExpiredLease_ReturnFalseAsync() {
        // Arrange
        var store = BuildStore(out var clock);
        var ttl = TimeSpan.FromSeconds(10);
        _ = await store.TryAcquireAsync<string>("key", "owner", ttl, TestContext!.CancellationToken).ConfigureAwait(false);

        // Act
        var missing = await store.RenewAsync("missing", "owner", ttl, TestContext!.CancellationToken).ConfigureAwait(false);
        var wrongOwner = await store.RenewAsync("key", "other", ttl, TestContext!.CancellationToken).ConfigureAwait(false);
        clock.Advance(ttl + TimeSpan.FromSeconds(1));
        var expired = await store.RenewAsync("key", "owner", ttl, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => missing.Should().BeFalse());
        MSTestAssertions.That(() => wrongOwner.Should().BeFalse());
        MSTestAssertions.That(() => expired.Should().BeFalse());
    }

    [TestMethod]
    public async Task Renew_AtTheExactExpiry_ReturnsFalseAsync() {
        // Arrange
        var store = BuildStore(out var clock);
        var ttl = TimeSpan.FromSeconds(10);
        _ = await store.TryAcquireAsync<string>("key", "owner", ttl, TestContext!.CancellationToken).ConfigureAwait(false);

        // Act
        clock.Advance(ttl);
        var renewed = await store.RenewAsync("key", "owner", ttl, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => renewed.Should().BeFalse());
    }

    [TestMethod]
    public async Task Renew_ZeroTimeToLive_ThrowsWithParameterNameAsync() {
        // Arrange
        var store = BuildStore(out _);

        // Act
        var thrown = await CaptureAsync(() => store.RenewAsync("key", "owner", TimeSpan.Zero, TestContext!.CancellationToken)).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<ArgumentOutOfRangeException>());
        MSTestAssertions.That(() => ((ArgumentOutOfRangeException)thrown!).ParamName.Should().Be("timeToLive"));
    }

    [TestMethod]
    public async Task Complete_MissingOrWrongOwner_ReturnsFalse_AndOwnerStoresResponseAsync() {
        // Arrange
        var store = BuildStore(out _);
        var ttl = TimeSpan.FromSeconds(10);
        _ = await store.TryAcquireAsync<string>("key", "owner", ttl, TestContext!.CancellationToken).ConfigureAwait(false);

        // Act
        var missing = await store.CompleteAsync("missing", "owner", "value", TestContext!.CancellationToken).ConfigureAwait(false);
        var wrongOwner = await store.CompleteAsync("key", "other", "value", TestContext!.CancellationToken).ConfigureAwait(false);
        var completed = await store.CompleteAsync("key", "owner", "value", TestContext!.CancellationToken).ConfigureAwait(false);
        var replay = await store.TryAcquireAsync<string>("key", "next", ttl, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => missing.Should().BeFalse());
        MSTestAssertions.That(() => wrongOwner.Should().BeFalse());
        MSTestAssertions.That(() => completed.Should().BeTrue());
        MSTestAssertions.That(() => replay.State.Should().Be(IdempotencyLeaseState.Completed));
        MSTestAssertions.That(() => replay.Response.Should().Be("value"));
    }

    [TestMethod]
    public async Task Release_MissingWrongOwnerAndOwner_BehaveAsync() {
        // Arrange
        var store = BuildStore(out _);
        var ttl = TimeSpan.FromSeconds(10);
        _ = await store.TryAcquireAsync<string>("key", "owner", ttl, TestContext!.CancellationToken).ConfigureAwait(false);

        // Act
        var missing = await store.ReleaseAsync("missing", "owner", TestContext!.CancellationToken).ConfigureAwait(false);
        var wrongOwner = await store.ReleaseAsync("key", "other", TestContext!.CancellationToken).ConfigureAwait(false);
        var released = await store.ReleaseAsync("key", "owner", TestContext!.CancellationToken).ConfigureAwait(false);
        var reacquired = await store.TryAcquireAsync<string>("key", "next", ttl, TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => missing.Should().BeFalse());
        MSTestAssertions.That(() => wrongOwner.Should().BeFalse());
        MSTestAssertions.That(() => released.Should().BeTrue());
        MSTestAssertions.That(() => reacquired.State.Should().Be(IdempotencyLeaseState.Acquired));
        MSTestAssertions.That(() => reacquired.Owner.Should().Be("next"));
    }

    private static IIdempotencyStore BuildStore(out ManualClock clock) {
        clock = new ManualClock();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddCqrsIdempotencyBehavior();

        return services.BuildServiceProvider().GetRequiredService<IIdempotencyStore>();
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

    private sealed class ManualClock : TimeProvider {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan by) => _now += by;
    }
}
