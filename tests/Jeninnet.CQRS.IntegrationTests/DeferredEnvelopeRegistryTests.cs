//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.IntegrationTests;

/// <summary>
/// CQRS-39 characterization: the generated registry rehydrates both deferred envelope shapes
/// without reflection, and reports unknown command types so the store can use its fallback.
/// </summary>
[TestClass]
public class DeferredEnvelopeRegistryTests {
    private static readonly DateTimeOffset _due = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void GeneratedRegistry_RehydratesVoidAndResultCommands() {
        // Arrange
        using var provider = BuildProvider();
        var serializer = provider.GetRequiredService<IMessageSerializer>();
        var factory = provider.GetRequiredService<IDeferredEnvelopeFactory>();

        // Act
        var resultCreated = factory.TryCreate(
            typeof(IntegrationTestCommand).FullName!,
            serializer.Serialize(new IntegrationTestCommand("Ping"), typeof(IntegrationTestCommand)),
            _due,
            null,
            1,
            null,
            out var resultEnvelope);
        var voidCreated = factory.TryCreate(
            typeof(IntegrationTestVoidCommand).AssemblyQualifiedName!,
            serializer.Serialize(new IntegrationTestVoidCommand("Ping"), typeof(IntegrationTestVoidCommand)),
            _due,
            null,
            1,
            null,
            out var voidEnvelope);

        // Assert: ICommand<T> uses the result envelope; ICommand uses the void envelope.
        MSTestAssertions.That(() => resultCreated.Should().BeTrue());
        MSTestAssertions.That(() => resultEnvelope.Should().BeOfType<DeferredResultEnvelope<IntegrationTestCommand, string>>());
        MSTestAssertions.That(() => ((IntegrationTestCommand)resultEnvelope!.Command).Name.Should().Be("Ping"));
        MSTestAssertions.That(() => voidCreated.Should().BeTrue());
        MSTestAssertions.That(() => voidEnvelope.Should().BeOfType<DeferredEnvelope<IntegrationTestVoidCommand>>());
        MSTestAssertions.That(() => ((IntegrationTestVoidCommand)voidEnvelope!.Command).Name.Should().Be("Ping"));
    }

    [TestMethod]
    public void GeneratedRegistry_WrapsAnAlreadyDeserializedCommand() {
        // Arrange
        using var provider = BuildProvider();
        var factory = provider.GetRequiredService<IDeferredEnvelopeFactory>();

        // Act: this is the path an upgrader uses when it replaces the command instance.
        var wrapped = factory.TryWrap(
            new IntegrationTestVoidCommand("Ping"),
            _due,
            null,
            1,
            null,
            out var envelope);

        // Assert
        MSTestAssertions.That(() => wrapped.Should().BeTrue());
        MSTestAssertions.That(() => envelope.Should().BeOfType<DeferredEnvelope<IntegrationTestVoidCommand>>());
    }

    [TestMethod]
    public void GeneratedRegistry_ReportsUnknownCommandTypes_ForTheReflectionFallback() {
        // Arrange
        using var provider = BuildProvider();
        var factory = provider.GetRequiredService<IDeferredEnvelopeFactory>();

        // Act
        var created = factory.TryCreate("Missing.Command, MissingAssembly", [], _due, null, 1, null, out var envelope);

        // Assert
        MSTestAssertions.That(() => created.Should().BeFalse());
        MSTestAssertions.That(() => envelope.Should().BeNull());
    }

    [TestMethod]
    public void GeneratedRegistry_RebuildsRecurringVoidEnvelopes() {
        // Arrange
        using var provider = BuildProvider();
        var serializer = provider.GetRequiredService<IMessageSerializer>();
        var factory = provider.GetRequiredService<IDeferredEnvelopeFactory>();
        var recurring = new RecurringEnvelopeMetadata("nightly", "0 2 * * *", TimeZoneInfo.Utc.Id, RecurringOverlapPolicy.Skip);

        // Act
        var created = factory.TryCreate(
            typeof(IntegrationTestVoidCommand).FullName!,
            serializer.Serialize(new IntegrationTestVoidCommand("Ping"), typeof(IntegrationTestVoidCommand)),
            _due,
            null,
            1,
            recurring,
            out var envelope);

        // Assert
        MSTestAssertions.That(() => created.Should().BeTrue());
        MSTestAssertions.That(() => envelope.Should().BeOfType<RecurringEnvelope<IntegrationTestVoidCommand>>());
        MSTestAssertions.That(() => ((RecurringEnvelope<IntegrationTestVoidCommand>)envelope!).UniqueKey.Should().Be("nightly"));
    }

    private static ServiceProvider BuildProvider() {
        var services = new ServiceCollection();
        services.AddCqrs();
        services.AddCqrsMessageSerialization();

        return services.BuildServiceProvider();
    }
}
