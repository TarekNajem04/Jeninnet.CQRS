//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Polyfills;

/// <summary>
/// Guard clauses shared by every package. Calls the BCL throw helpers where they exist and
/// reproduces their argument-name and message behavior on netstandard2.0.
/// </summary>
internal static class Guard {
    /// <summary>Throws <see cref="ArgumentNullException"/> when <paramref name="value"/> is null.</summary>
    /// <param name="value">The value to check.</param>
    /// <param name="parameterName">Captured automatically at the call site.</param>
    public static void NotNull([System.Diagnostics.CodeAnalysis.NotNull] object? value, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(value))] string? parameterName = null) {
        if(value is null) {
            throw new ArgumentNullException(parameterName);
        }
    }

    /// <summary>Throws when a string is null, empty, or only whitespace.</summary>
    /// <param name="value">The value to check.</param>
    /// <param name="parameterName">Captured automatically at the call site.</param>
    public static void NotNullOrWhiteSpace([System.Diagnostics.CodeAnalysis.NotNull] string? value, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(value))] string? parameterName = null) {
        if(value is null || value.Trim().Length is 0) {
            throw new ArgumentException("The value cannot be an empty string or composed entirely of whitespace.", parameterName);
        }
    }

    /// <summary>Throws when a string is null or empty.</summary>
    /// <param name="value">The value to check.</param>
    /// <param name="parameterName">Captured automatically at the call site.</param>
    public static void NotNullOrEmpty([System.Diagnostics.CodeAnalysis.NotNull] string? value, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(value))] string? parameterName = null) {
        if(value is null || value.Length is 0) {
            throw new ArgumentException("The value cannot be an empty string.", parameterName);
        }
    }

    /// <summary>Throws <see cref="ObjectDisposedException"/> when <paramref name="condition"/> is true.</summary>
    /// <param name="condition">Whether the object has been disposed.</param>
    /// <param name="instance">The object used to name the exception.</param>
    public static void NotDisposed(bool condition, object instance) {
#if NETSTANDARD2_0 || NET462
        if(condition) {
            throw new ObjectDisposedException(instance.GetType().FullName);
        }
#else
        ObjectDisposedException.ThrowIf(condition, instance);
#endif
    }
}
