//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.NetStandard.Tests;

/// <summary>
/// Runs the netstandard2.0 contract suite from a modern test host.
/// </summary>
[TestClass]
public sealed class NetStandardContractTests {
    public TestContext? TestContext { get; set; }

    [TestMethod]
    public Task DispatchAndPipeline_RunOnTheNetStandardAssetsAsync() =>
        NetStandard.Contracts.ContractSuite.DispatchAsync(TestContext!.CancellationToken);

    [TestMethod]
    public Task CachingBehavior_RunsOnTheNetStandardAssetAsync() =>
        NetStandard.Contracts.ContractSuite.CachingAsync(TestContext!.CancellationToken);
}