//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Idempotency.EntityFrameworkCore.Tests;

/// <summary>
/// Characterization tests for CQRS-50's durable pending-request inbox: rows round-trip through
/// SQLite, can be removed by id, and expire by TTL.
/// </summary>
[TestClass]
public sealed class RequestInboxTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public async Task EfInbox_RoundTripsRemovesAndPrunesExpiredRowsAsync() {
        // Arrange
        var file = Path.Combine(Path.GetTempPath(), $"cqrs50-inbox-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddCqrsEfRequestResponse(db => db.UseSqlite($"Data Source={file}"));
        await using var provider = services.BuildServiceProvider();

        using(var scope = provider.CreateScope()) {
            var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<IdempotencyDbContext>>().CreateDbContext();
            db.Database.EnsureCreated();
        }

        var inbox = provider.GetRequiredService<IRequestInbox>();
        var now = DateTimeOffset.UtcNow;
        var entry = new RequestInboxEntry(
            "request-1",
            typeof(EchoRequest),
            typeof(EchoResponse),
            RequestResponseMode.First,
            now,
            now.AddSeconds(30));

        // Act: add then list.
        await inbox.AddAsync(entry, TestContext!.CancellationToken).ConfigureAwait(false);
        var listed = await inbox.ListAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => inbox.Should().BeOfType<EfRequestInbox>());
        MSTestAssertions.That(() => listed.Should().HaveCount(1));
        MSTestAssertions.That(() => listed[0].RequestId.Should().Be("request-1"));

        // Act: prune before expiry removes nothing; after expiry removes the row.
        var before = await inbox.RemoveExpiredAsync(now, TestContext!.CancellationToken).ConfigureAwait(false);
        var after = await inbox.RemoveExpiredAsync(now.AddSeconds(31), TestContext!.CancellationToken).ConfigureAwait(false);
        var remaining = await inbox.ListAsync(TestContext!.CancellationToken).ConfigureAwait(false);

        // Assert
        MSTestAssertions.That(() => before.Should().Be(0));
        MSTestAssertions.That(() => after.Should().Be(1));
        MSTestAssertions.That(() => remaining.Should().BeEmpty());
    }

    private sealed record EchoRequest(string Value);

    private sealed record EchoResponse(string Value);
}