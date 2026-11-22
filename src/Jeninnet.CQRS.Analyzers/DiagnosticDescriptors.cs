//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Analyzers;

/// <summary>
/// Diagnostic descriptors for Jeninnet.CQRS handler-misuse analyzers.
/// </summary>
public static class DiagnosticDescriptors {
    /// <summary>Two or more handlers claim the same message.</summary>
    public const string DUPLICATE_HANDLER_ID = "CQRS0001";

    /// <summary>A handler never forwards its cancellation token.</summary>
    public const string UNUSED_CANCELLATION_TOKEN_ID = "CQRS0002";

    /// <summary>One handler class claims more than one message.</summary>
    public const string MULTIPLE_MESSAGES_ID = "CQRS0003";

    private const string CATEGORY = "Usage";

    /// <summary>Gets the duplicate-handler diagnostic.</summary>
    public static readonly DiagnosticDescriptor DuplicateHandler = new(
        DUPLICATE_HANDLER_ID,
        "Duplicate handler",
        "Message '{0}' has more than one handler ('{1}' and '{2}'); registration order decides which one runs",
        CATEGORY,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Two handlers for the same message make dispatch ambiguous: only one wins, chosen by registration order. Keep a single handler per message.",
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    /// <summary>Gets the unused-cancellation-token diagnostic.</summary>
    public static readonly DiagnosticDescriptor UnusedCancellationToken = new(
        UNUSED_CANCELLATION_TOKEN_ID,
        "CancellationToken is never used",
        "Parameter '{0}' is never forwarded; pass it to the awaited call or remove it if cancellation is intentionally unsupported",
        CATEGORY,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A handler that ignores its cancellation token cannot be cancelled. Forward the token to the awaited operation.");

    /// <summary>Gets the multiple-messages diagnostic.</summary>
    public static readonly DiagnosticDescriptor MultipleMessages = new(
        MULTIPLE_MESSAGES_ID,
        "Handler handles multiple messages",
        "'{0}' handles both '{1}' and '{2}'; split it into one handler per message",
        CATEGORY,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "One handler class claiming several messages is usually a copy-paste mistake and complicates registration. Prefer one handler per message.");
}