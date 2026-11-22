//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

namespace Jeninnet.CQRS.Benchmarks;

/// <summary>
/// Runs the suite and writes a normalized snapshot so CI can compare allocations and
/// baseline-relative timings across machines. Absolute medians are published for reference.
/// </summary>
public static class Program {
    public static int Main(string[] args) {
        var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args).ToArray();
        var benchmarks = new SortedDictionary<string, BenchmarkResult>(StringComparer.Ordinal);

        foreach(var summary in summaries) {
            var baseline = summary.Reports.FirstOrDefault(static report =>
                report.BenchmarkCase.Descriptor.WorkloadMethod.GetCustomAttribute<BenchmarkAttribute>()?.Baseline is true);
            var baselineMedian = baseline?.ResultStatistics?.Median ?? 0;

            foreach(var report in summary.Reports) {
                var key = $"{report.BenchmarkCase.Descriptor.Type.FullName}.{report.BenchmarkCase.Descriptor.WorkloadMethod.Name}";
                var median = report.ResultStatistics?.Median ?? 0;

                benchmarks[key] = new BenchmarkResult(
                    median,
                    baselineMedian > 0 ? median / baselineMedian : 1,
                    AllocatedBytes(report));
            }
        }

        var outputPath = Environment.GetEnvironmentVariable("BENCHMARK_OUTPUT")
            ?? Path.Combine("docs", "benchmarks", "current.json");
        var directory = Path.GetDirectoryName(outputPath);

        if(!string.IsNullOrEmpty(directory)) {
            Directory.CreateDirectory(directory);
        }

        var snapshot = new BenchmarkSnapshot(
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            DateTimeOffset.UtcNow,
            benchmarks);
        File.WriteAllText(outputPath, JsonSerializer.Serialize(snapshot, SnapshotJson.Options));
        Console.WriteLine($"Wrote {benchmarks.Count} benchmark result(s) to {outputPath}");

        return 0;
    }

    private static long AllocatedBytes(BenchmarkReport report) {
        foreach(var metric in report.Metrics) {
            if(metric.Key.Contains("Alloc", StringComparison.OrdinalIgnoreCase)) {
                return (long)metric.Value.Value;
            }
        }

        return 0;
    }
}

/// <summary>The committed/current benchmark snapshot schema.</summary>
/// <param name="Environment">The OS description of the capture machine.</param>
/// <param name="Framework">The .NET runtime description.</param>
/// <param name="GeneratedAtUtc">Capture time.</param>
/// <param name="Benchmarks">Results keyed by type.method.</param>
public sealed record BenchmarkSnapshot(
    string Environment,
    string Framework,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyDictionary<string, BenchmarkResult> Benchmarks);

/// <summary>One benchmark result.</summary>
/// <param name="MedianNanoseconds">Absolute median, for the published README table.</param>
/// <param name="RatioToBaseline">Median divided by the class baseline; hardware-independent.</param>
/// <param name="AllocatedBytes">Allocated bytes per operation.</param>
public sealed record BenchmarkResult(
    double MedianNanoseconds,
    double RatioToBaseline,
    long AllocatedBytes);

internal static class SnapshotJson {
    internal static readonly JsonSerializerOptions Options = new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };
}