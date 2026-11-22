//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.AspNetCore.Tests;

/// <summary>
/// Minimal-API request binding: JSON body, route values, query strings, simple conversions, and
/// the invalid-body paths.
/// </summary>
[TestClass]
public sealed class CqrsRequestBinderTests {
    private static readonly string[] _expectedTags = ["a", "b"];

    [TestMethod]
    public async Task BindAsync_BodylessRequest_UsesRouteAndQueryValuesAsync() {
        var context = new DefaultHttpContext();
        context.Request.RouteValues["id"] = "6f9619ff-8b86-d011-b42d-00c04fc964ff";
        context.Request.QueryString = new QueryString("?count=3&name=route&tags=a&tags=b");

        var result = await CqrsRequestBinder.BindAsync<TestRequest>(context, readBody: false, CancellationToken.None).ConfigureAwait(false);

        MSTestAssertions.That(() => result.Error.Should().BeNull());
        MSTestAssertions.That(() => result.Request.Should().NotBeNull());

        var request = result.Request!;
        MSTestAssertions.That(() => request.Id.Should().Be(Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff")));
        MSTestAssertions.That(() => request.Count.Should().Be(3));
        MSTestAssertions.That(() => request.Name.Should().Be("route"));
        MSTestAssertions.That(() => request.Tags!.Should().ContainInOrder(_expectedTags));
    }

    [TestMethod]
    public async Task BindAsync_BodyAndRoute_RouteWinsAndQueryOverridesBodyAsync() {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"name\":\"body\",\"count\":1}"));
        context.Request.RouteValues["count"] = "7";
        context.Request.QueryString = new QueryString("?name=query");

        var result = await CqrsRequestBinder.BindAsync<TestRequest>(context, readBody: true, CancellationToken.None).ConfigureAwait(false);

        MSTestAssertions.That(() => result.Error.Should().BeNull());
        MSTestAssertions.That(() => result.Request.Should().NotBeNull());

        var request = result.Request!;
        MSTestAssertions.That(() => request.Name.Should().Be("query"));
        MSTestAssertions.That(() => request.Count.Should().Be(7));
    }

    [TestMethod]
    public async Task BindAsync_InvalidJson_ReturnsBindingErrorAsync() {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{"));

        var result = await CqrsRequestBinder.BindAsync<TestRequest>(context, readBody: true, CancellationToken.None).ConfigureAwait(false);

        MSTestAssertions.That(() => result.Request.Should().BeNull());
        MSTestAssertions.That(() => result.Error.Should().NotBeNull());
    }

    [TestMethod]
    public async Task BindAsync_NonObjectJson_ReturnsBindingErrorAsync() {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("[1,2,3]"));

        var result = await CqrsRequestBinder.BindAsync<TestRequest>(context, readBody: true, CancellationToken.None).ConfigureAwait(false);

        MSTestAssertions.That(() => result.Request.Should().BeNull());
        MSTestAssertions.That(() => result.Error.Should().NotBeNull());
    }

    [TestMethod]
    public async Task BindAsync_UnknownKeysAreIgnoredAsync() {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?unknown=value&count=2");

        var result = await CqrsRequestBinder.BindAsync<TestRequest>(context, readBody: false, CancellationToken.None).ConfigureAwait(false);

        MSTestAssertions.That(() => result.Error.Should().BeNull());
        MSTestAssertions.That(() => result.Request.Should().NotBeNull());

        var request = result.Request!;
        MSTestAssertions.That(() => request.Count.Should().Be(2));
    }

    private sealed class TestRequest {
        public Guid Id { get; set; }
        public string? Name { get; set; }
        public int Count { get; set; }
        public string[]? Tags { get; set; }
    }
}
