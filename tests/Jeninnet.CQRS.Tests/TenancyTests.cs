//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>A message used by the in-memory tenant header round-trip.</summary>
/// <param name="Value">The payload value.</param>
public sealed record TenantTestMessage(string Value);

/// <summary>
/// Characterization tests for CQRS-52: ambient tenant scopes, per-tenant cache/idempotency/
/// rate-limit/concurrency partitions, strict missing-tenant enforcement, and the tenant header
/// on the in-memory transport.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TenancyTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task AmbientScope_FlowsAcrossAwait_AndRestoresNestedTenantAsync() {
        // Arrange
        var context = new AsyncLocalTenantContext();

        // Act + Assert
        MSTestAssertions.That(() => context.Current.Should().BeNull());

        using(context.BeginScope("acme")) {
            await Task.Yield();
            MSTestAssertions.That(() => context.Current.Should().Be("acme"));

            using(context.BeginScope("globex")) {
                await Task.Yield();
                MSTestAssertions.That(() => context.Current.Should().Be("globex"));
            }

            MSTestAssertions.That(() => context.Current.Should().Be("acme"));
        }

        MSTestAssertions.That(() => context.Current.Should().BeNull());
    }

    [TestMethod]
    public async Task CachingBehavior_IsolatesTheSameCacheKeyPerTenantAsync() {
        // Arrange
        await using var provider = BuildProvider(services => services.AddCqrsCachingBehavior());
        var context = provider.GetRequiredService<ITenantContext>();
        var behavior = provider.GetRequiredService<IPipelineBehavior<TenantCacheableQuery, string>>();
        var executions = 0;

        Task<string> Handle(TenantCacheableQuery _, CancellationToken __) {
            executions++;

            return Task.FromResult(context.Current!);
        }

        // Act: the same cache key is read and written under two tenants.
        string acmeFirst;
        string acmeSecond;
        string globexFirst;
        string globexSecond;

        using(context.BeginScope("acme")) {
            acmeFirst = await behavior.HandleAsync(new TenantCacheableQuery("orders"), Handle, TestContext!.CancellationToken).ConfigureAwait(false);
            acmeSecond = await behavior.HandleAsync(new TenantCacheableQuery("orders"), Handle, TestContext!.CancellationToken).ConfigureAwait(false);
        }

        using(context.BeginScope("globex")) {
            globexFirst = await behavior.HandleAsync(new TenantCacheableQuery("orders"), Handle, TestContext!.CancellationToken).ConfigureAwait(false);
            globexSecond = await behavior.HandleAsync(new TenantCacheableQuery("orders"), Handle, TestContext!.CancellationToken).ConfigureAwait(false);
        }

        // Assert: each tenant hits its own cache entry; no value leaks across.
        MSTestAssertions.That(() => acmeFirst.Should().Be("acme"));
        MSTestAssertions.That(() => acmeSecond.Should().Be("acme"));
        MSTestAssertions.That(() => globexFirst.Should().Be("globex"));
        MSTestAssertions.That(() => globexSecond.Should().Be("globex"));
        MSTestAssertions.That(() => executions.Should().Be(2));
    }

    [TestMethod]
    public async Task IdempotencyBehavior_IsolatesTheSameKeyPerTenantAsync() {
        // Arrange
        await using var provider = BuildProvider(services => services.AddCqrsIdempotencyBehavior());
        var context = provider.GetRequiredService<ITenantContext>();
        var behavior = provider.GetRequiredService<IPipelineBehavior<TenantIdempotentCommand, string>>();
        var executions = 0;

        Task<string> Handle(TenantIdempotentCommand _, CancellationToken __) {
            executions++;

            return Task.FromResult(context.Current!);
        }

        // Act: the same caller-supplied idempotency key under two tenants.
        string acme;
        string globex;

        using(context.BeginScope("acme")) {
            acme = await behavior.HandleAsync(new TenantIdempotentCommand("same-key"), Handle, TestContext!.CancellationToken).ConfigureAwait(false);
        }

        using(context.BeginScope("globex")) {
            globex = await behavior.HandleAsync(new TenantIdempotentCommand("same-key"), Handle, TestContext!.CancellationToken).ConfigureAwait(false);
        }

        // Assert
        MSTestAssertions.That(() => acme.Should().Be("acme"));
        MSTestAssertions.That(() => globex.Should().Be("globex"));
        MSTestAssertions.That(() => executions.Should().Be(2));
    }

    [TestMethod]
    public async Task RateLimitBehavior_IsolatesTheSamePartitionPerTenantAsync() {
        // Arrange
        await using var provider = BuildProvider(services => services.AddCqrsRateLimiting(new RateLimitOptions {
            PermitLimit = 1,
            Window = TimeSpan.FromHours(1),
        }));
        var context = provider.GetRequiredService<ITenantContext>();
        var behavior = provider.GetRequiredService<IPipelineBehavior<TenantCommand, string>>();
        static Task<string> Handle(TenantCommand command, CancellationToken _) => Task.FromResult(command.Value);

        // Act: each tenant gets its own one-permit window; the second acme call is shed.
        string firstAcme;
        string firstGlobex;
        Exception? rejected;

        using(context.BeginScope("acme")) {
            firstAcme = await behavior.HandleAsync(new TenantCommand("a"), Handle, TestContext!.CancellationToken).ConfigureAwait(false);
        }

        using(context.BeginScope("globex")) {
            firstGlobex = await behavior.HandleAsync(new TenantCommand("b"), Handle, TestContext!.CancellationToken).ConfigureAwait(false);
        }

        using(context.BeginScope("acme")) {
            rejected = await CaptureAsync(() => behavior.HandleAsync(new TenantCommand("c"), Handle, TestContext!.CancellationToken)).ConfigureAwait(false);
        }

        // Assert
        MSTestAssertions.That(() => firstAcme.Should().Be("a"));
        MSTestAssertions.That(() => firstGlobex.Should().Be("b"));
        MSTestAssertions.That(() => rejected.Should().BeOfType<RateLimitRejectedException>());
        MSTestAssertions.That(() => ((RateLimitRejectedException)rejected!).Key.Should().Be($"acme:{typeof(TenantCommand).FullName}"));
    }

    [TestMethod]
    public async Task PartitionedConcurrency_DoesNotSerialiseAcrossTenantsAsync() {
        // Arrange
        await using var provider = BuildProvider(services => services.AddCqrsPartitionedConcurrency(new PartitionedConcurrencyOptions {
            ConcurrencyLimit = 1,
            FailFastWhenBusy = true,
        }));
        var context = provider.GetRequiredService<ITenantContext>();
        var behavior = provider.GetRequiredService<IPipelineBehavior<TenantCommand, string>>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<string> Blocking(TenantCommand _, CancellationToken cancellationToken) {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

            return "held";
        }

        // Act: acme holds the partition; globex must still enter with the same request key.
        Task<string> held;
        using(context.BeginScope("acme")) {
            held = behavior.HandleAsync(new TenantCommand("same"), Blocking, TestContext!.CancellationToken);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext!.CancellationToken).ConfigureAwait(false);
        }

        Task<string> other;
        using(context.BeginScope("globex")) {
            other = behavior.HandleAsync(new TenantCommand("same"), (_, _) => Task.FromResult("other"), TestContext!.CancellationToken);
        }

        var otherResult = await other.WaitAsync(TimeSpan.FromSeconds(5), TestContext!.CancellationToken).ConfigureAwait(false);
        release.TrySetResult();
        var heldResult = await held.WaitAsync(TimeSpan.FromSeconds(5), TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => otherResult.Should().Be("other"));
        MSTestAssertions.That(() => heldResult.Should().Be("held"));
    }

    [TestMethod]
    public async Task InMemoryTransport_RoundTripsTheTenantHeaderAsync() {
        // Arrange
        var handler = new RecordingTransportHandler();
        await using var provider = BuildProvider(services => {
            services.AddCqrsMessageSerialization();
            services.AddCqrsInMemoryTransport();
            services.AddSingleton<IMessageTransportHandler>(handler);
        }, requireTenant: true);
        var context = provider.GetRequiredService<ITenantContext>();
        var transport = provider.GetRequiredService<IMessageTransport>();
        await transport.StartAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        // Act
        using(context.BeginScope("acme")) {
            await transport.PublishAsync(new TenantTestMessage("hello"), cancellationToken: TestContext!.CancellationToken).ConfigureAwait(false);
        }

        // Assert: the payload, envelope field, and header all carry the tenant.
        MSTestAssertions.That(() => handler.Envelopes.Should().HaveCount(1));
        MSTestAssertions.That(() => handler.Envelopes[0].TenantId.Should().Be("acme"));
        MSTestAssertions.That(() => handler.Envelopes[0].Headers[TransportHeaders.Tenant].Should().Be("acme"));
    }

    [TestMethod]
    public async Task StrictMode_ThrowsWhenNoTenantScopeIsActiveAsync() {
        // Arrange
        await using var provider = BuildProvider(services => services.AddCqrsCachingBehavior(), requireTenant: true);
        var behavior = provider.GetRequiredService<IPipelineBehavior<TenantCacheableQuery, string>>();

        // Act
        var thrown = await CaptureAsync(() => behavior.HandleAsync(
            new TenantCacheableQuery("no-tenant"),
            (_, _) => Task.FromResult("unexpected"),
            TestContext!.CancellationToken)).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<TenantRequiredException>());
    }

    [TestMethod]
    public async Task OptionalMode_AllowsNoTenantScopeAsync() {
        // Arrange
        await using var provider = BuildProvider(services => services.AddCqrsCachingBehavior(), requireTenant: false);
        var behavior = provider.GetRequiredService<IPipelineBehavior<TenantCacheableQuery, string>>();
        var executions = 0;

        // Act
        var first = await behavior.HandleAsync(
            new TenantCacheableQuery("optional"),
            (_, _) => {
                executions++;

                return Task.FromResult("value");
            },
            TestContext!.CancellationToken).ConfigureAwait(false);
        var second = await behavior.HandleAsync(
            new TenantCacheableQuery("optional"),
            (_, _) => {
                executions++;

                return Task.FromResult("value");
            },
            TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => first.Should().Be("value"));
        MSTestAssertions.That(() => second.Should().Be("value"));
        MSTestAssertions.That(() => executions.Should().Be(1));
    }

    private static ServiceProvider BuildProvider(Action<IServiceCollection> configure, bool requireTenant = true) {
        var services = new ServiceCollection();
        services.AddCqrsMultiTenancy(_ => null, options => options.RequireTenant = requireTenant);
        configure(services);

        return services.BuildServiceProvider();
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

    private sealed record TenantCacheableQuery(string CacheKey) : IQuery<string>, ICacheable<string> {
        public MemoryCacheEntryOptions? CacheOptions => new() { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1) };
    }

    private sealed record TenantIdempotentCommand(string Key) : IIdempotentCommand<string> {
        public string IdempotencyKey => Key;
    }

    private sealed record TenantCommand(string Value) : ICommand<string>;

    private sealed class RecordingTransportHandler : IMessageTransportHandler {
        private readonly Lock _sync = new();
        private readonly List<TransportEnvelope> _envelopes = [];

        internal IReadOnlyList<TransportEnvelope> Envelopes {
            get {
                lock(_sync) {
                    return [.. _envelopes];
                }
            }
        }

        public Task HandleAsync(TransportEnvelope envelope, CancellationToken cancellationToken = default) {
            lock(_sync) {
                _envelopes.Add(envelope);
            }

            return Task.CompletedTask;
        }
    }
}
