//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

namespace Jeninnet.CQRS.Sample.AotSmoke;

/// <summary>One durable deferred row, serialized as a JSON line.</summary>
public sealed record PersistedDeferredCommand(
    string Key,
    string CommandType,
    string Payload,
    DateTimeOffset DueUtc,
    DateTimeOffset? ExpiresAtUtc,
    int Version);

/// <summary>
/// Reflection-free <see cref="IDeferredStore"/> for the AOT smoke: payloads are staged as JSON
/// lines, and recovery asks the generated <see cref="IDeferredEnvelopeFactory"/> to rebuild each
/// command. No <c>Type.GetType</c>, no <c>Activator</c>, no dynamic code.
/// </summary>
public sealed class FileDeferredStore(string path, IMessageSerializer serializer, IDeferredEnvelopeFactory factory) : IDeferredStore {
    private readonly string _path = path ?? throw new ArgumentNullException(nameof(path));
    private readonly IMessageSerializer _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    private readonly IDeferredEnvelopeFactory _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public async Task<string> StageAsync(IDeferredEnvelope envelope, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(envelope);

        var command = envelope.Command;
        var key = Guid.NewGuid().ToString("N");
        var record = new PersistedDeferredCommand(
            key,
            command.GetType().AssemblyQualifiedName ?? command.GetType().FullName ?? command.GetType().Name,
            Convert.ToBase64String(_serializer.Serialize(command, command.GetType())),
            envelope.DueUtc,
            envelope.ExpiresAtUtc,
            envelope.Version);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try {
            await File.AppendAllTextAsync(_path, JsonSerializer.Serialize(record, AotJsonContext.Default.PersistedDeferredCommand) + Environment.NewLine, cancellationToken).ConfigureAwait(false);
        }
        finally {
            _gate.Release();
        }

        return key;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(string Key, IDeferredEnvelope Envelope)>> ListAsync(CancellationToken cancellationToken = default) {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try {
            var records = await ReadAsync(cancellationToken).ConfigureAwait(false);
            var listed = new List<(string Key, IDeferredEnvelope Envelope)>(records.Count);

            foreach(var record in records) {
                if(!_factory.TryCreate(
                    record.CommandType,
                    Convert.FromBase64String(record.Payload),
                    record.DueUtc,
                    record.ExpiresAtUtc,
                    record.Version,
                    recurring: null,
                    out var envelope)) {
                    throw new InvalidOperationException(
                        $"The generated registry does not cover '{record.CommandType}'. Declare the command in this compilation for a trimmed replay.");
                }

                listed.Add((record.Key, envelope));
            }

            return listed;
        }
        finally {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task CompleteAsync(string key, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try {
            var records = await ReadAsync(cancellationToken).ConfigureAwait(false);
            await WriteAsync(records.Where(record => record.Key != key).ToList(), cancellationToken).ConfigureAwait(false);
        }
        finally {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<int> PruneExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default) {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try {
            var records = await ReadAsync(cancellationToken).ConfigureAwait(false);
            var kept = records.Where(record => record.ExpiresAtUtc is null || record.ExpiresAtUtc > now).ToList();
            var removed = records.Count - kept.Count;

            if(removed > 0) {
                await WriteAsync(kept, cancellationToken).ConfigureAwait(false);
            }

            return removed;
        }
        finally {
            _gate.Release();
        }
    }

    private async Task<List<PersistedDeferredCommand>> ReadAsync(CancellationToken cancellationToken) {
        if(!File.Exists(_path)) {
            return [];
        }

        var lines = await File.ReadAllLinesAsync(_path, cancellationToken).ConfigureAwait(false);
        var records = new List<PersistedDeferredCommand>(lines.Length);

        foreach(var line in lines) {
            if(string.IsNullOrWhiteSpace(line)) {
                continue;
            }

            records.Add(JsonSerializer.Deserialize(line, AotJsonContext.Default.PersistedDeferredCommand)
                ?? throw new InvalidOperationException("The deferred file contains a corrupt row."));
        }

        return records;
    }

    private async Task WriteAsync(IReadOnlyList<PersistedDeferredCommand> records, CancellationToken cancellationToken) {
        var lines = records.Select(static record => JsonSerializer.Serialize(record, AotJsonContext.Default.PersistedDeferredCommand));
        await File.WriteAllLinesAsync(_path, lines, cancellationToken).ConfigureAwait(false);
    }
}
