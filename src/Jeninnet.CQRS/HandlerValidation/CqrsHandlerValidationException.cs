//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.HandlerValidation;

/// <summary>
/// Thrown at startup when handler validation finds problems. Carries every finding, not just
/// the first one, so a misconfiguration can be fixed in one pass.
/// </summary>
public sealed class CqrsHandlerValidationException : InvalidOperationException {
    /// <summary>
    /// Initializes the exception from the full set of findings.
    /// </summary>
    /// <param name="findings">All findings; never empty in practice.</param>
    public CqrsHandlerValidationException(IReadOnlyList<HandlerValidationFinding> findings)
        : base(BuildMessage(findings)) {
        Guard.NotNull(findings);
        Findings = findings;
    }

    /// <summary>Gets every finding that was collected.</summary>
    public IReadOnlyList<HandlerValidationFinding> Findings { get; }

    private static string BuildMessage(IReadOnlyList<HandlerValidationFinding> findings) {
        Guard.NotNull(findings);

        var builder = new System.Text.StringBuilder();
        builder.Append("CQRS handler validation failed with ").Append(findings.Count).Append(" finding(s):");

        foreach(var finding in findings) {
            builder.AppendLine().Append(" - ").Append(finding);
        }

        return builder.ToString();
    }
}
