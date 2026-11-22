//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.RoutingSlips;

/// <summary>
/// Routing-slip persistence seam. The in-memory store is the default; the EF Core package replaces
/// it with a durable table. Saves are optimistic: the expected version is checked and the new
/// version written together, or <see cref="RoutingSlipConcurrencyException"/> is thrown.
/// </summary>
public interface IRoutingSlipStore {
    /// <summary>Inserts a new slip row and returns it at version 1.</summary>
    /// <param name="record">The new row; its version is ignored.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <exception cref="RoutingSlipConcurrencyException">The slip id already exists.</exception>
    Task<RoutingSlipRecord> CreateAsync(RoutingSlipRecord record, CancellationToken cancellationToken = default);

    /// <summary>Loads one slip, or returns <see langword="null"/> when the id is unknown.</summary>
    /// <param name="slipId">The slip id.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<RoutingSlipRecord?> LoadAsync(string slipId, CancellationToken cancellationToken = default);

    /// <summary>Saves progress only when <paramref name="expectedVersion"/> still matches.</summary>
    /// <param name="record">The mutated row.</param>
    /// <param name="expectedVersion">The version read before the mutation.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>The stored row at the new version.</returns>
    /// <exception cref="RoutingSlipConcurrencyException">Another writer saved first.</exception>
    Task<RoutingSlipRecord> SaveAsync(RoutingSlipRecord record, long expectedVersion, CancellationToken cancellationToken = default);

    /// <summary>Lists lightweight rows for progress inspection and health checks.</summary>
    /// <param name="definitionName">An optional definition filter.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<RoutingSlipSnapshot>> ListAsync(string? definitionName = null, CancellationToken cancellationToken = default);

    /// <summary>Counts rows with one status; used by the active gauge.</summary>
    /// <param name="status">The status to count.</param>
    /// <param name="cancellationToken">Cancels the count.</param>
    Task<long> CountAsync(RoutingSlipStatus status, CancellationToken cancellationToken = default);

    /// <summary>Removes a slip row; a missing row is a no-op.</summary>
    /// <param name="slipId">The slip id.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    Task DeleteAsync(string slipId, CancellationToken cancellationToken = default);
}
