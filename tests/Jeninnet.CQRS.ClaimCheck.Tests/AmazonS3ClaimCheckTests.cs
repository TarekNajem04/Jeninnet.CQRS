//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.ClaimCheck.Tests;

/// <summary>
/// Runs the shared claim-check contract against Amazon S3 using LocalStack. The suite becomes
/// inconclusive when Docker is unavailable. Set <c>CQRS_S3_SERVICE_URL</c> to use an external
/// S3-compatible endpoint; <c>CQRS_S3_ACCESS_KEY</c>/<c>CQRS_S3_SECRET_KEY</c> override the
/// development credentials.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class AmazonS3ClaimCheckTests : ClaimCheckContractTests {
    private static LocalStackContainer? _container;
    private static AmazonS3Client? _client;
    private static AmazonS3ClaimCheckStore? _store;
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

    /// <summary>Starts LocalStack and creates the claim bucket.</summary>
    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext testContext) {
        var externalServiceUrl = Environment.GetEnvironmentVariable("CQRS_S3_SERVICE_URL");

        if(!string.IsNullOrWhiteSpace(externalServiceUrl)) {
            await InitializeClientAsync(externalServiceUrl, testContext.CancellationToken).ConfigureAwait(false);
            _available = true;

            return;
        }

        if(!await DockerAvailability.IsAvailableAsync(testContext.CancellationToken).ConfigureAwait(false)) {
            return;
        }

        _container = new LocalStackBuilder("localstack/localstack:4.14")
            .WithEnvironment("SERVICES", "s3")
            .Build();

        await _container.StartAsync(testContext.CancellationToken).ConfigureAwait(false);
        await InitializeClientAsync(_container.GetConnectionString(), testContext.CancellationToken).ConfigureAwait(false);
        _available = true;
    }

    /// <summary>Stops LocalStack and releases the client.</summary>
    [ClassCleanup]
    public static async Task ClassCleanupAsync() {
        _client?.Dispose();

        if(_container is not null) {
            await _container.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task InitializeClientAsync(string serviceUrl, CancellationToken cancellationToken) {
        var config = new AmazonS3Config {
            ForcePathStyle = true,
            RegionEndpoint = RegionEndpoint.USEast1,
            ServiceURL = serviceUrl,
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };

        var accessKey = Environment.GetEnvironmentVariable("CQRS_S3_ACCESS_KEY") ?? "test";
        var secretKey = Environment.GetEnvironmentVariable("CQRS_S3_SECRET_KEY") ?? "test";

        _client = new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), config);
        var bucket = "cqrs62-" + Guid.NewGuid().ToString("N")[..16].ToLowerInvariant();

        await _client.PutBucketAsync(
            new PutBucketRequest { BucketName = bucket },
            cancellationToken).ConfigureAwait(false);

        _store = new AmazonS3ClaimCheckStore(_client, bucket);
    }

    private static void RequireDocker() {
        if(!_available || _store is null) {
            Assert.Inconclusive("No S3 endpoint is available; the Amazon S3 claim-check tests were skipped.");
        }
    }
}
