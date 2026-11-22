//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Retention;

/// <summary>Well-known retention metric names and tag keys.</summary>
public static class RetentionDiagnosticNames {
    /// <summary>Gets the meter name owned by the retention sweeper.</summary>
    public const string MeterName = "Jeninnet.CQRS.Retention";

    /// <summary>Gets the archived-rows counter name.</summary>
    public const string Archived = "cqrs.retention.archived";

    /// <summary>Gets the deleted-rows counter name.</summary>
    public const string Deleted = "cqrs.retention.deleted";

    /// <summary>Gets the sweep-failure counter name.</summary>
    public const string Faults = "cqrs.retention.faults";

    /// <summary>Gets the tag key carrying the store name.</summary>
    public const string StoreTag = "cqrs.retention.store";

    /// <summary>Gets the tag key marking a dry-run measurement.</summary>
    public const string DryRunTag = "cqrs.retention.dry_run";
}

/// <summary>Per-store retention counters tagged with the store name and dry-run state.</summary>
public sealed class RetentionMetrics : IDisposable {
    private readonly Meter _meter;
    private readonly Counter<long> _archived;
    private readonly Counter<long> _deleted;
    private readonly Counter<long> _faults;

    /// <summary>Initializes the retention instruments.</summary>
    public RetentionMetrics() {
        _meter = new Meter(RetentionDiagnosticNames.MeterName, "1.0.0");
        _archived = _meter.CreateCounter<long>(RetentionDiagnosticNames.Archived, unit: "{rows}", description: "Rows moved to a retention archive.");
        _deleted = _meter.CreateCounter<long>(RetentionDiagnosticNames.Deleted, unit: "{rows}", description: "Rows removed by retention.");
        _faults = _meter.CreateCounter<long>(RetentionDiagnosticNames.Faults, unit: "{events}", description: "Retention sweeps that failed.");
    }

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();

    internal void Record(RetentionBatchResult result) {
        var tags = new[] {
            new KeyValuePair<string, object?>(RetentionDiagnosticNames.StoreTag, result.Store),
            new KeyValuePair<string, object?>(RetentionDiagnosticNames.DryRunTag, result.DryRun),
        };

        if(result.Archived > 0) {
            _archived.Add(result.Archived, tags);
        }

        if(result.Deleted > 0) {
            _deleted.Add(result.Deleted, tags);
        }
    }

    internal void RecordFault(string store) =>
        _faults.Add(1, new KeyValuePair<string, object?>(RetentionDiagnosticNames.StoreTag, store));
}

/// <summary>Point-in-time view of the sweeper for health checks and tests.</summary>
/// <param name="Enabled">Whether the sweeper is enabled and has an interval.</param>
/// <param name="StoreCount">Configured policy count.</param>
/// <param name="LastAttemptUtc">The last sweep attempt, if any.</param>
/// <param name="LastSuccessUtc">The last sweep with no failures, if any.</param>
/// <param name="LastError">The last failure message, if any.</param>
public sealed record RetentionSweeperSnapshot(
    bool Enabled,
    int StoreCount,
    DateTimeOffset? LastAttemptUtc,
    DateTimeOffset? LastSuccessUtc,
    string? LastError);
