//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RequestResponse;

/// <summary>
/// One pending request in the inbox. The client owns the row's lifetime: it is registered before
/// sending and removed when the request completes, times out, or fails.
/// </summary>
/// <param name="RequestId">The request/correlation id.</param>
/// <param name="RequestType">The request message type.</param>
/// <param name="ResponseType">The expected response message type.</param>
/// <param name="Mode">First or all.</param>
/// <param name="CreatedAtUtc">When the request was registered.</param>
/// <param name="ExpiresAtUtc">When the client stops waiting.</param>
/// <param name="TenantId">The tenant that issued the request, or <see langword="null"/>.</param>
public sealed record RequestInboxEntry(
    string RequestId,
    Type RequestType,
    Type ResponseType,
    RequestResponseMode Mode,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string? TenantId = null);

/// <summary>
/// Pending-request store. The in-memory implementation is the default; the EF Core package
/// replaces it with a durable table. Response delivery itself stays in-process; the row provides
/// TTL cleanup and operator visibility.
/// </summary>
public interface IRequestInbox {
    /// <summary>Registers a pending request.</summary>
    /// <param name="entry">The pending request.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task AddAsync(RequestInboxEntry entry, CancellationToken cancellationToken = default);

    /// <summary>Removes a pending request; a missing id is a no-op.</summary>
    /// <param name="requestId">The request id.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    Task RemoveAsync(string requestId, CancellationToken cancellationToken = default);

    /// <summary>Lists pending requests; used by tests and operational tooling.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<RequestInboxEntry>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes rows whose expiry is at or before <paramref name="now"/>.</summary>
    /// <param name="now">The current UTC time.</param>
    /// <param name="cancellationToken">Cancels the cleanup.</param>
    Task<int> RemoveExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}
