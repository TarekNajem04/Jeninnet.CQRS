//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.EntityFrameworkCore;

/// <summary>
/// One durable saga row: the composite key (state type + saga id), the serialized state, and the
/// version token. State bytes are produced by the configured <see cref="Serialization.IMessageSerializer"/>,
/// so custom formats behave like the in-memory store.
/// </summary>
public sealed class SagaRecordEntity {
    /// <summary>Gets or sets the assembly-qualified state type name. Part of the primary key.</summary>
    public string StateType { get; set; } = string.Empty;

    /// <summary>Gets or sets the saga correlation id. Part of the primary key.</summary>
    public string SagaId { get; set; } = string.Empty;

    /// <summary>Gets or sets the serialized state payload.</summary>
    public byte[] State { get; set; } = [];

    /// <summary>Gets or sets the state name, duplicated out of the payload for health queries.</summary>
    public string CurrentState { get; set; } = string.Empty;

    /// <summary>Gets or sets the JSON compensation progress (completed/compensated step ids).</summary>
    public string Progress { get; set; } = "{}";

    /// <summary>Gets or sets the optimistic-concurrency version.</summary>
    public long Version { get; set; }

    /// <summary>Gets or sets when the saga row was created, in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Gets or sets when the saga row last changed, in UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
