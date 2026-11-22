//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Batch;

/// <summary>One failed item in a batch dispatch.</summary>
/// <param name="Index">The zero-based input index.</param>
/// <param name="Error">The exception raised by that item.</param>
public sealed record BatchDispatchFailure(int Index, Exception Error);

/// <summary>
/// The aggregated failure of a batch dispatch: one exception carrying every failed item in input
/// order. The batch always runs to completion before this is thrown by
/// <see cref="BatchDispatchResult{TResponse}.ThrowIfFailed"/>.
/// </summary>
public sealed class BatchDispatchException : Exception {
    /// <summary>
    /// Initializes the exception from the failed items.
    /// </summary>
    /// <param name="failures">Failed items in input order; never empty in practice.</param>
    public BatchDispatchException(IReadOnlyList<BatchDispatchFailure> failures)
        : base(BuildMessage(failures)) {
        Guard.NotNull(failures);
        Failures = failures;
    }

    /// <summary>Gets every failed item in input order.</summary>
    public IReadOnlyList<BatchDispatchFailure> Failures { get; }

    private static string BuildMessage(IReadOnlyList<BatchDispatchFailure> failures) {
        Guard.NotNull(failures);

        var indices = string.Join(", ", failures.Select(static failure => failure.Index));
        var first = failures.Count > 0 ? failures[0].Error.Message : null;

        return $"Batch dispatch failed for {failures.Count} item(s) at index(es) {indices}." +
            (first is null ? string.Empty : $" First error: {first}");
    }
}
