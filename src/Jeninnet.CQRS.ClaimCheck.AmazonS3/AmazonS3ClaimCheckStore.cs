//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.ClaimCheck.AmazonS3;

/// <summary>
/// Amazon S3 <see cref="IClaimCheckStore"/>. Object keys are content-addressed with
/// <see cref="ClaimCheckKey"/> and the serializer media type is stored as the object content
/// type.
/// </summary>
/// <remarks>
/// S3 encrypts new objects with SSE-S3 by default. Bucket policies or an explicit SSE-KMS
/// configuration provide customer-managed keys; this provider deliberately does not own that
/// deployment decision. Use a bucket policy that blocks public access and limits the runtime role
/// to the claim-check prefix.
/// </remarks>
public sealed class AmazonS3ClaimCheckStore : IClaimCheckStore, IClaimCheckStoreMaintenance {
    private readonly IAmazonS3 _client;
    private readonly string _bucketName;

    /// <summary>Initializes a store over an existing S3 client and bucket.</summary>
    /// <param name="client">The S3 client. Never <see langword="null"/>.</param>
    /// <param name="bucketName">The existing bucket.</param>
    public AmazonS3ClaimCheckStore(IAmazonS3 client, string bucketName) {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        Guard.NotNullOrWhiteSpace(bucketName);

        _bucketName = bucketName;
    }

    /// <inheritdoc />
    public async Task<string> PutAsync(ReadOnlyMemory<byte> payload, string contentType, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(contentType);

        var key = ClaimCheckKey.Compute(payload, contentType);

        using var stream = new MemoryStream(payload.ToArray(), writable: false);
        var request = new PutObjectRequest {
            BucketName = _bucketName,
            Key = key,
            InputStream = stream,
            ContentType = contentType,
            AutoCloseStream = false,
        };

        await _client.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);

        return key;
    }

    /// <inheritdoc />
    public async Task<ClaimCheckPayload?> GetAsync(string key, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);

        try {
            using var response = await _client.GetObjectAsync(
                new GetObjectRequest { BucketName = _bucketName, Key = key },
                cancellationToken).ConfigureAwait(false);
            using var payload = new MemoryStream();

            await response.ResponseStream.CopyToAsync(payload, 81920, cancellationToken).ConfigureAwait(false);

            return new ClaimCheckPayload(
                payload.ToArray(),
                response.Headers.ContentType ?? "application/octet-stream");
        }
        catch(AmazonS3Exception exception) when(exception.StatusCode is HttpStatusCode.NotFound) {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string key, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);

        try {
            await _client.DeleteObjectAsync(
                new DeleteObjectRequest { BucketName = _bucketName, Key = key },
                cancellationToken).ConfigureAwait(false);
        }
        catch(AmazonS3Exception exception) when(exception.StatusCode is HttpStatusCode.NotFound) {
            // S3 treats a missing object as success; a missing bucket is equally a no-op here.
        }
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(CancellationToken cancellationToken = default) {
        try {
            var count = 0;
            string? continuationToken = null;

            do {
                var response = await ListAsync(continuationToken, cancellationToken).ConfigureAwait(false);
                count += response.S3Objects?.Count ?? 0;
                continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
            }
            while(!StringCompat.IsNullOrEmpty(continuationToken));

            return count;
        }
        catch(AmazonS3Exception exception) when(exception.StatusCode is HttpStatusCode.NotFound) {
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
            string? continuationToken = null;

            do {
                var response = await ListAsync(continuationToken, cancellationToken).ConfigureAwait(false);

                foreach(var item in response.S3Objects ?? []) {
                    cancellationToken.ThrowIfCancellationRequested();

                    if(referenced.Contains(item.Key)) {
                        continue;
                    }

                    if(item.LastModified is not { } lastModified || lastModified.ToUniversalTime() >= olderThanUtc.UtcDateTime) {
                        continue;
                    }

                    await _client.DeleteObjectAsync(
                        new DeleteObjectRequest { BucketName = _bucketName, Key = item.Key },
                        cancellationToken).ConfigureAwait(false);
                    deleted++;
                }

                continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
            }
            while(!StringCompat.IsNullOrEmpty(continuationToken));
        }
        catch(AmazonS3Exception exception) when(exception.StatusCode is HttpStatusCode.NotFound) {
            return 0;
        }

        return deleted;
    }

    private Task<ListObjectsV2Response> ListAsync(string? continuationToken, CancellationToken cancellationToken) =>
        _client.ListObjectsV2Async(
            new ListObjectsV2Request {
                BucketName = _bucketName,
                ContinuationToken = continuationToken,
            },
            cancellationToken);
}