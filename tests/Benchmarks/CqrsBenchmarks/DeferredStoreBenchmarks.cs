//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Benchmarks;

/// <summary>
/// In-memory deferred store: one stage plus one complete per operation.
/// </summary>
[MemoryDiagnoser]
public class DeferredStoreBenchmarks {
    private InMemoryDeferredStore _store = null!;
    private DeferredEnvelope<Ping> _envelope = null!;

    [GlobalSetup]
    public void Setup() {
        _store = new InMemoryDeferredStore();
        _envelope = new DeferredEnvelope<Ping>(new Ping("bench"), DateTimeOffset.UtcNow, null);
    }

    [Benchmark]
    public async Task StageAndComplete() {
        var key = await _store.StageAsync(_envelope, default).ConfigureAwait(false);
        await _store.CompleteAsync(key, default).ConfigureAwait(false);
    }

    public sealed record Ping(string Name) : ICommand;
}