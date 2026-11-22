//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.ClaimCheck;

/// <summary>
/// External storage for payloads that are too large to keep in a durable row. The three-method
/// contract is deliberate: retention and failure policy belong to the caller.
/// </summary>
public interface IClaimCheckStore {
    /// <summary>Stores <paramref name="payload"/> and returns its key.</summary>
    Task<string> PutAsync(ReadOnlyMemory<byte> payload, string contentType, CancellationToken cancellationToken = default);

    /// <summary>Reads the payload for <paramref name="key"/>, or <see langword="null"/> when missing.</summary>
    Task<ClaimCheckPayload?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Deletes the payload for <paramref name="key"/>; missing keys are a no-op.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}