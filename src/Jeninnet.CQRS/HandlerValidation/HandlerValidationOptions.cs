//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.HandlerValidation;

/// <summary>
/// Options for startup handler validation.
/// </summary>
public sealed class HandlerValidationOptions {
    /// <summary>
    /// Gets or sets whether findings abort startup. Defaults to <see langword="true"/>;
    /// set to <see langword="false"/> to only log the report.
    /// </summary>
    public bool FailOnStartup { get; set; } = true;

    /// <summary>
    /// Gets or sets whether captive dependencies (singleton → scoped) are checked.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool CheckCaptiveDependencies { get; set; } = true;

    /// <summary>
    /// Gets or sets whether duplicate handlers for one message are errors.
    /// Notification handlers are always allowed to be many. Defaults to <see langword="true"/>.
    /// </summary>
    public bool CheckDuplicateHandlers { get; set; } = true;
}