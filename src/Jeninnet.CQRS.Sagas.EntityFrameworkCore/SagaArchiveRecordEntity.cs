//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sagas.EntityFrameworkCore;

/// <summary>Archived copy of a terminal saga row, with the archival timestamp.</summary>
public sealed class SagaArchiveRecordEntity {
    /// <summary>Gets or sets the assembly-qualified state type name.</summary>
    public string StateType { get; set; } = string.Empty;

    /// <summary>Gets or sets the saga correlation id.</summary>
    public string SagaId { get; set; } = string.Empty;

    /// <summary>Gets or sets the serialized state payload.</summary>
    public byte[] State { get; set; } = [];

    /// <summary>Gets or sets the terminal state name.</summary>
    public string CurrentState { get; set; } = string.Empty;

    /// <summary>Gets or sets the compensation progress JSON.</summary>
    public string Progress { get; set; } = "{}";

    /// <summary>Gets or sets the original optimistic-concurrency version.</summary>
    public long Version { get; set; }

    /// <summary>Gets or sets when the saga row was created, in UTC.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Gets or sets when the saga row last changed, in UTC.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>Gets or sets when the row moved to the archive, in UTC.</summary>
    public DateTimeOffset ArchivedAtUtc { get; set; }
}