//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.ClaimCheck;

/// <summary>
/// Applies the claim-check threshold and hides the optional store: durable stores ask the policy
/// to stage large payloads and to resolve references, so the inline path stays allocation-light
/// and the disabled case stays a no-op.
/// </summary>
/// <param name="options">The threshold options. Never <see langword="null"/>.</param>
/// <param name="stores">Registered claim-check stores; the last registration wins, matching DI's single-resolution rule. Empty means disabled.</param>
public sealed class ClaimCheckPolicy(IOptions<ClaimCheckOptions> options, IEnumerable<IClaimCheckStore> stores) {
    private readonly IClaimCheckStore? _store = (stores ?? throw new ArgumentNullException(nameof(stores))).LastOrDefault();

    /// <summary>Gets whether a claim-check store is registered.</summary>
    public bool IsEnabled => _store is not null;

    /// <summary>Gets the inline threshold in bytes.</summary>
    public int ThresholdBytes { get; } = Validate((options ?? throw new ArgumentNullException(nameof(options))).Value);

    /// <summary>
    /// Stages <paramref name="payload"/> when claim-check is enabled and the payload is larger
    /// than the threshold. Returns <see langword="null"/> when the row should keep it inline.
    /// </summary>
    /// <param name="payload">The serialized bytes.</param>
    /// <param name="contentType">The serializer content type.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public async Task<string?> StageIfLargeAsync(ReadOnlyMemory<byte> payload, string contentType, CancellationToken cancellationToken) {
        if(_store is null || payload.Length <= ThresholdBytes) {
            return null;
        }

        return await _store.PutAsync(payload, contentType, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the bytes for a row: <see langword="null"/> means the row keeps them inline.
    /// A missing reference or a content-type mismatch fails loudly instead of deserializing
    /// garbage.
    /// </summary>
    /// <param name="claimCheckKey">The row's claim-check key, or <see langword="null"/>.</param>
    /// <param name="contentType">The current serializer content type.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<byte[]?> GetClaimAsync(string? claimCheckKey, string contentType, CancellationToken cancellationToken) {
        if(claimCheckKey is null) {
            return null;
        }

        if(_store is null) {
            throw new InvalidOperationException($"A row references claim-check '{claimCheckKey}', but no {nameof(IClaimCheckStore)} is registered.");
        }

        var claim = await _store.GetAsync(claimCheckKey, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Claim-check '{claimCheckKey}' was not found. Was the claim-check store reset?");

        if(!string.Equals(claim.ContentType, contentType, StringComparison.Ordinal)) {
            throw new InvalidOperationException($"Claim-check '{claimCheckKey}' was written as '{claim.ContentType}', but the current serializer reports '{contentType}'.");
        }

        return claim.Payload;
    }

    /// <summary>Deletes a referenced payload; missing keys and a disabled store are no-ops.</summary>
    /// <param name="claimCheckKey">The row's claim-check key, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    public Task DeleteAsync(string? claimCheckKey, CancellationToken cancellationToken) =>
        claimCheckKey is null || _store is null
            ? Task.CompletedTask
            : _store.DeleteAsync(claimCheckKey, cancellationToken);

    private static int Validate(ClaimCheckOptions options) {
        if(options.ThresholdBytes < 0) {
            throw new ArgumentOutOfRangeException(nameof(options), options.ThresholdBytes, "ThresholdBytes must not be negative.");
        }

        return options.ThresholdBytes;
    }
}
