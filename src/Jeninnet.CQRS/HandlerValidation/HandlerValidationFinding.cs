//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.HandlerValidation;

/// <summary>
/// The kind of problem found while validating handler registrations.
/// </summary>
public enum HandlerValidationKind {
    /// <summary>A registered handler (or behavior) cannot be constructed from the container.</summary>
    UnresolvableDependency,

    /// <summary>A singleton handler depends on a scoped service (captive dependency).</summary>
    CaptiveDependency,

    /// <summary>A message has more than one handler registered.</summary>
    DuplicateHandler,

    /// <summary>An open-generic registration could not be closed for a known request type.</summary>
    OpenGenericUncloseable,
}

/// <summary>
/// One validation finding: what is wrong and where.
/// </summary>
/// <param name="Kind">The finding category.</param>
/// <param name="ServiceType">The registration being validated.</param>
/// <param name="ImplementationType">The implementation behind it, when known.</param>
/// <param name="Message">A human-readable explanation naming the culprit.</param>
public sealed record HandlerValidationFinding(
    HandlerValidationKind Kind,
    Type ServiceType,
    Type? ImplementationType,
    string Message) {
    /// <summary>Gets the finding category.</summary>
    public HandlerValidationKind Kind { get; } = Kind;

    /// <summary>Gets the registration being validated.</summary>
    public Type ServiceType { get; } = ServiceType ?? throw new ArgumentNullException(nameof(ServiceType));

    /// <summary>Gets the implementation behind the registration, when known.</summary>
    public Type? ImplementationType { get; } = ImplementationType;

    /// <summary>Gets the human-readable explanation naming the culprit.</summary>
    public string Message { get; } = Message ?? throw new ArgumentNullException(nameof(Message));

    /// <inheritdoc />
    public override string ToString() =>
        ImplementationType is null
            ? $"{Kind}: {Message}"
            : $"{Kind} ({ImplementationType.Name}): {Message}";
}
