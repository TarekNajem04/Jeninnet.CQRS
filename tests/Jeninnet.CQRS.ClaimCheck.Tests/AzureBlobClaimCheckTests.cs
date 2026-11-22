//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.ClaimCheck.Tests;

/// <summary>
/// Runs the shared claim-check contract against Azure Blob Storage. The suite starts Azurite in a
/// container and becomes inconclusive when Docker is unavailable. Set
/// <c>CQRS_AZURITE_CONNECTION_STRING</c> to use an external Azurite instance instead.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class AzureBlobClaimCheckTests : ClaimCheckContractTests {
    private const string AccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";
    private static IContainer? _container;
    private static AzureBlobClaimCheckStore? _store;
    private static bool _available;

    /// <inheritdoc />
    protected override IClaimCheckStore Store {
        get {
            RequireDocker();

            return _store!;
        }
    }

    /// <inheritdoc />
    protected override IClaimCheckStoreMaintenance Maintenance => (IClaimCheckStoreMaintenance)Store;

    /// <summary>Starts Azurite and creates the claim container.</summary>
    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext) {
        var externalConnectionString = Environment.GetEnvironmentVariable("CQRS_AZURITE_CONNECTION_STRING");

        if(!string.IsNullOrWhiteSpace(externalConnectionString)) {
            var externalClient = new BlobContainerClient(externalConnectionString, "claim-check");

            await externalClient.CreateIfNotExistsAsync(cancellationToken: testContext.CancellationToken).ConfigureAwait(false);

            _store = new AzureBlobClaimCheckStore(externalClient, createContainerIfMissing: false);
            _available = true;

            return;
        }

        if(!await DockerAvailability.IsAvailableAsync(testContext.CancellationToken).ConfigureAwait(false)) {
            return;
        }

        _container = new ContainerBuilder("mcr.microsoft.com/azure-storage/azurite:latest")
            .WithPortBinding(10000, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(10000))
            .Build();

        await _container.StartAsync(testContext.CancellationToken).ConfigureAwait(false);

        var connectionString =
            $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey={AccountKey};" +
            $"BlobEndpoint=http://127.0.0.1:{_container.GetMappedPublicPort(10000)}/devstoreaccount1;";
        var containerClient = new BlobContainerClient(connectionString, "claim-check");

        await containerClient.CreateIfNotExistsAsync(cancellationToken: testContext.CancellationToken).ConfigureAwait(false);

        _store = new AzureBlobClaimCheckStore(containerClient, createContainerIfMissing: false);
        _available = true;
    }

    /// <summary>Stops the Azurite container.</summary>
    [ClassCleanup]
    public static async Task ClassCleanupAsync() {
        if(_container is not null) {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void RequireDocker() {
        if(!_available || _store is null) {
            Assert.Inconclusive("No Azurite endpoint is available; the Azure Blob claim-check tests were skipped.");
        }
    }
}
