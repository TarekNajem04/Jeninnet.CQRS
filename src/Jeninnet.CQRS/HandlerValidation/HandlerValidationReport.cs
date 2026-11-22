//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.HandlerValidation;

/// <summary>
/// Result of validating the registered handlers.
/// </summary>
public sealed class HandlerValidationReport {
    /// <summary>Initializes the report.</summary>
    /// <param name="findings">All findings, in a deterministic order.</param>
    public HandlerValidationReport(IReadOnlyList<HandlerValidationFinding> findings) {
        Guard.NotNull(findings);
        Findings = findings;
    }

    /// <summary>Gets every finding.</summary>
    public IReadOnlyList<HandlerValidationFinding> Findings { get; }

    /// <summary>Gets a value indicating whether the registrations are healthy.</summary>
    public bool IsValid => Findings.Count is 0;
}

/// <summary>
/// Validates handler/behavior registrations against the live container.
/// </summary>
public interface IHandlerValidator {
    /// <summary>Runs the validation and returns every finding.</summary>
    HandlerValidationReport Validate();
}
