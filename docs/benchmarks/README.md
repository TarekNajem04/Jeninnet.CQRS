# Benchmarks

Measured dispatch, resilience, caching, deferred-delivery, and EF Core store costs. A benchmark
is a test with a threshold: CI runs the dispatch fast set and fails on a **20% regression** in
allocated bytes or in the baseline-relative time ratio.

## Published numbers

Captured on Windows 10.0.26300 with .NET 11.0.0 using the default BenchmarkDotNet job
(`warmupCount: 5`, `iterationCount: 10` for dispatch). Medians; allocations are managed bytes per
operation including the handler's `Task`.

| Scenario | Median | Allocated |
|---|---:|---:|
| Bare handler dispatch (`CommandDispatcher`) | 1,059 ns | 584 B |
| Dispatch with four pipeline behaviors | 1,592 ns (1.50× bare) | 1,392 B |
| Retry path, one failure then success | 19.03 µs | 1,872 B |
| In-memory cache hit (`CachingBehavior`) | 58.6 ns | 72 B |
| In-memory deferred stage + complete | 297 ns | 160 B |
| EF Core SQLite deferred stage + list + complete | 6.23 ms | 178,794 B |

Absolute medians are a snapshot, not an SLA: compare ratios and allocations. The baseline file
is `docs/benchmarks/baseline.json`; a run writes the same schema to
`docs/benchmarks/current.json` (or `BENCHMARK_OUTPUT`).

## Running

```powershell
# Full suite (several minutes; dispatch uses 10 iterations)
dotnet run --project tests/Benchmarks/CqrsBenchmarks -c Release -- --filter "*"

# Quick reproduction (3 warmups / 3 iterations; wider variance by design)
dotnet run --project tests/Benchmarks/CqrsBenchmarks -c Release -- --filter "*" --job short

# What CI gates (dispatch ratio + allocations, default job)
dotnet run --project tests/Benchmarks/CqrsBenchmarks -c Release -- --filter "*Dispatch*"
```

BenchmarkDotNet 0.16 is still a preview because the 0.15 line does not recognize the .NET 11
runtime. The suite targets `net11.0` because the EF Core SQLite store is net11-only.

## CI gate

`scripts/benchmarks/compare.ps1` compares every benchmark present in both files:

- **Allocated bytes** must not exceed baseline × 1.20.
- **Ratio to the class baseline** must not exceed baseline × 1.20. The ratio is
  hardware-independent, which is why absolute nanoseconds are not compared across machines.
- Missing benchmark: ignored when the current run is a filtered fast set; an empty intersection
  fails, so a broken suite cannot pass silently.

The CI job runs `dotnet run --project tests/Benchmarks/CqrsBenchmarks --configuration Release --
--filter "*Dispatch*"` under a 20-minute timeout, then the comparison. Keep `baseline.json`
updated when a change intentionally moves the numbers, and call that out in the PR.
