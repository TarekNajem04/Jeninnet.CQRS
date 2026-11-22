//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.ClaimCheck.AzureBlob;

/// <summary>
/// Azure Blob Storage <see cref="IClaimCheckStore"/>. Blob names are content-addressed with
/// <see cref="ClaimCheckKey"/>; the content type is stored in the blob's HTTP headers.
/// </summary>
/// <remarks>
/// Azure Storage encrypts blobs at rest with service-managed keys by default. Use a
/// customer-managed key or a Key Vault-backed account when compliance requires it; that is a
/// deployment setting, not something this package can enforce.
/// </remarks>
public sealed class AzureBlobClaimCheckStore : IClaimCheckStore, IClaimCheckStoreMaintenance {
    private readonly BlobContainerClient _container;
    private readonly bool _createContainerIfMissing;
    private int _containerReady;

    /// <summary>Initializes a store over an existing container client.</summary>
    /// <param name="container">The blob container client. Never <see langword="null"/>.</param>
    /// <param name="createContainerIfMissing">Whether the first write creates the container.</param>
    public AzureBlobClaimCheckStore(BlobContainerClient container, bool createContainerIfMissing = true) {
        _container = container ?? throw new ArgumentNullException(nameof(container));
        _createContainerIfMissing = createContainerIfMissing;
    }

    /// <summary>Initializes a store from configured options.</summary>
    /// <param name="options">The provider options with a connection string and container name.</param>
    public AzureBlobClaimCheckStore(IOptions<AzureBlobClaimCheckOptions> options)
        : this(CreateContainer(options), (options ?? throw new ArgumentNullException(nameof(options))).Value.CreateContainerIfMissing) {
    }

    /// <inheritdoc />
    public async Task<string> PutAsync(ReadOnlyMemory<byte> payload, string contentType, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(contentType);

        await EnsureContainerAsync(cancellationToken).ConfigureAwait(false);

        var key = ClaimCheckKey.Compute(payload, contentType);
        var blob = _container.GetBlobClient(key);
        var uploadOptions = new BlobUploadOptions {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
        };

        await blob.UploadAsync(new BinaryData(payload.ToArray()), uploadOptions, cancellationToken).ConfigureAwait(false);

        return key;
    }

    /// <inheritdoc />
    public async Task<ClaimCheckPayload?> GetAsync(string key, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);

        try {
            var response = await _container.GetBlobClient(key).DownloadContentAsync(cancellationToken).ConfigureAwait(false);
            var contentType = response.Value.Details.ContentType ?? "application/octet-stream";

            return new ClaimCheckPayload(response.Value.Content.ToArray(), contentType);
        }
        catch(RequestFailedException exception) when(exception.Status is 404) {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);

        try {
            await _container.GetBlobClient(key)
                .DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch(RequestFailedException exception) when(exception.Status is 404) {
            // A missing container means missing blobs: delete is a no-op.
        }
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken cancellationToken = default) {
        try {
            var count = 0;

            await foreach(var _ in _container.GetBlobsAsync(cancellationToken: cancellationToken).ConfigureAwait(false)) {
                count++;
            }

            return count;
        }
        catch(RequestFailedException exception) when(exception.Status is 404) {
            return 0;
        }
    }

    /// <inheritdoc />
    public async Task<int> CleanupAsync(
        DateTimeOffset olderThanUtc,
        IReadOnlyCollection<string> referencedKeys,
        CancellationToken cancellationToken = default) {
        Guard.NotNull(referencedKeys);

        var referenced = new HashSet<string>(referencedKeys, StringComparer.Ordinal);
        var deleted = 0;

        try {
            await foreach(var blob in _container.GetBlobsAsync(cancellationToken: cancellationToken).ConfigureAwait(false)) {
                if(referenced.Contains(blob.Name)) {
                    continue;
                }

                if(blob.Properties.LastModified is not { } lastModified || lastModified >= olderThanUtc) {
                    continue;
                }

                var response = await _container.GetBlobClient(blob.Name)
                    .DeleteIfExistsAsync(DeleteSnapshotsOption.IncludeSnapshots, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if(response.Value) {
                    deleted++;
                }
            }
        }
        catch(RequestFailedException exception) when(exception.Status is 404) {
            return 0;
        }

        return deleted;
    }

    private static BlobContainerClient CreateContainer(IOptions<AzureBlobClaimCheckOptions> options) {
        Guard.NotNull(options);

        var value = options.Value;

        Guard.NotNullOrWhiteSpace(value.ConnectionString);
        Guard.NotNullOrWhiteSpace(value.ContainerName);

        return new BlobContainerClient(value.ConnectionString, value.ContainerName);
    }

    private async Task EnsureContainerAsync(CancellationToken cancellationToken) {
        if(!_createContainerIfMissing || Volatile.Read(ref _containerReady) is 1) {
            return;
        }

        await _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        Volatile.Write(ref _containerReady, 1);
    }
}