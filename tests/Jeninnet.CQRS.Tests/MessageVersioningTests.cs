//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// Characterization tests for CQRS-36 message versioning: a v1 payload upgrades to v2 through
/// one registered upgrader, an unknown version fails loudly instead of being silently accepted,
/// and the pump dead-letters an unknown-version envelope rather than dropping it.
/// </summary>
[TestClass]
public class MessageVersioningTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public void Policy_UpgradesV1ToV2_AndLeavesCurrentMessagesUntouched() {
        // Arrange
        var policy = new MessageUpgradePolicy([new V1ToV2Upgrader()]);
        var v1 = new VersionedMessage("order-1", null);
        var v2 = new VersionedMessage("order-1", "upgraded");

        // Act
        var upgraded = (VersionedMessage)policy.Upgrade(v1, typeof(VersionedMessage), 1);
        var current = (VersionedMessage)policy.Upgrade(v2, typeof(VersionedMessage), 2);

        // Assert
        MSTestAssertions.That(() => policy.CurrentVersion(typeof(VersionedMessage)).Should().Be(2));
        MSTestAssertions.That(() => upgraded.Note.Should().Be("upgraded"));
        MSTestAssertions.That(() => ReferenceEquals(current, v2).Should().BeTrue());
    }

    [TestMethod]
    public void Policy_UnknownNewerVersion_FailsLoudly() {
        // Arrange
        var policy = new MessageUpgradePolicy([new V1ToV2Upgrader()]);

        // Act
        var thrown = Capture(() => policy.Upgrade(new VersionedMessage("x", null), typeof(VersionedMessage), 99));

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<UnknownMessageVersionException>());
        MSTestAssertions.That(() => ((UnknownMessageVersionException)thrown!).StoredVersion.Should().Be(99));
        MSTestAssertions.That(() => ((UnknownMessageVersionException)thrown!).CurrentVersion.Should().Be(2));
    }

    [TestMethod]
    public void Policy_DuplicateStartingVersion_IsRejected() {
        // Arrange / Act
        var thrown = Capture(() => _ = new MessageUpgradePolicy([new V1ToV2Upgrader(), new V1ToV2Upgrader()]));

        // Assert
        MSTestAssertions.That(() => thrown.Should().BeOfType<ArgumentException>());
    }

    [TestMethod]
    public async Task Pump_DeadLetters_AnUnknownVersionEnvelope_InsteadOfDroppingItAsync() {
        // Arrange: an envelope that fails before dispatch, as a durable reader would produce.
        var failure = new UnknownMessageVersionException(typeof(VersionedMessage), 99, 2);
        var inner = new DeferredResultEnvelope<VersionedMessage, string>(new VersionedMessage("x", null), DateTimeOffset.UtcNow, null, 99);
        var hostile = new UnknownVersionEnvelope(inner, failure);
        var calls = new List<string>();
        var deadLetters = new InMemoryDeadLetterStore(Options.Create(new DeadLetterOptions()));
        var services = new ServiceCollection();
        services.AddSingleton(calls);
        services.AddSingleton<IDeadLetterStore>(deadLetters);
        services.AddTransient<ICommandDispatcher, CommandDispatcher>();
        services.AddTransient<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICqrsBroker, CqrsBroker>();
        services.AddCqrsDeferredDelivery();
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IDeferredStore>();
        await store.StageAsync(hostile, TestContext!.CancellationToken);
        var dispatcher = provider.GetRequiredService<IDeferredDispatcher>();
        await ((IHostedService)dispatcher).StartAsync(TestContext!.CancellationToken);

        try {
            // Act: the pump's recovery path picks up the staged hostile envelope.
            await SpinUntilAsync(async () => (await deadLetters.ListAsync(TestContext!.CancellationToken).ConfigureAwait(false)).Count == 1, "dead letter recorded");

            // Assert: the fault is data, not a lost row.
            var entry = (await deadLetters.ListAsync(TestContext!.CancellationToken).ConfigureAwait(false)).Single();
            MSTestAssertions.That(() => entry.LastErrorType.Should().Contain(nameof(UnknownMessageVersionException)));
            MSTestAssertions.That(() => calls.Should().BeEmpty());
        }
        finally {
            await ((IHostedService)dispatcher).StopAsync(default).ConfigureAwait(false);
        }
    }

    private static async Task SpinUntilAsync(Func<Task<bool>> condition, string what) {
        var deadline = Environment.TickCount64 + 5000;

        while(!await condition().ConfigureAwait(false)) {
            if(Environment.TickCount64 > deadline) {
                throw new TimeoutException($"timed out waiting for: {what}");
            }

            await Task.Yield();
        }
    }

    public sealed record VersionedMessage(string Name, string? Note) : ICommand<string>;

    private sealed class V1ToV2Upgrader : MessageUpgrader<VersionedMessage> {
        public override int FromVersion => 1;

        public override int ToVersion => 2;

        protected override VersionedMessage UpgradeCore(VersionedMessage message) =>
            message with { Note = "upgraded" };
    }

    private static Exception? Capture(Action action) {
        try {
            action();

            return null;
        }
        catch(Exception exception) {
            return exception;
        }
    }
}
