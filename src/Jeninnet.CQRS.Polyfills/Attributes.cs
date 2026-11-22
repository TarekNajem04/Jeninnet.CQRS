//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
// Polyfills for compiler-required and annotation types that netstandard2.0 does not ship.
// Internal by design: these types are an implementation detail of each assembly that links
// this file, and duplicating internal types across assemblies is safe.
#if NETSTANDARD2_0 || NET462
namespace System.Runtime.CompilerServices {
    /// <summary>Marks an init-only setter; required by records and init accessors on netstandard2.0.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter, Inherited = false)]
    internal sealed class IsExternalInit : Attribute { // NOSONAR: Polyfill type name mirrors the BCL type it replaces.
    }

    /// <summary>Captures the source text of an argument expression.</summary>
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
    internal sealed class CallerArgumentExpressionAttribute(string parameterName) : Attribute {
        /// <summary>Gets the parameter name whose expression should be captured.</summary>
        public string ParameterName { get; } = parameterName;
    }

    /// <summary>Marks a member that must be set by an object initializer.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute {
    }

    /// <summary>Marks a constructor that sets all required members.</summary>
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute {
    }
}

namespace System.Diagnostics.CodeAnalysis {
    /// <summary>Specifies that a parameter must not be null even when its type allows it.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue, Inherited = false)]
    internal sealed class DisallowNullAttribute : Attribute {
    }

    /// <summary>Specifies that an output is not null when the method returns the given value.</summary>
    [AttributeUsage(AttributeTargets.Parameter, Inherited = false)]
    internal sealed class NotNullWhenAttribute(bool returnValue) : Attribute {
        /// <summary>Gets the return value that guarantees a non-null output.</summary>
        public bool ReturnValue { get; } = returnValue;
    }

    /// <summary>Specifies that a parameter is non-null after the method returns.</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.ReturnValue, Inherited = false)]
    internal sealed class NotNullAttribute : Attribute {
    }

    /// <summary>Indicates that a member requires dynamic code generation.</summary>
    [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
    internal sealed class RequiresDynamicCodeAttribute(string message) : Attribute {
        /// <summary>Gets the explanation shown to callers.</summary>
        public string Message { get; } = message;

        /// <summary>Gets or sets a link to more information.</summary>
        public string? Url { get; set; }
    }

    /// <summary>Indicates that a member requires unreferenced code.</summary>
    [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
    internal sealed class RequiresUnreferencedCodeAttribute(string message) : Attribute {
        /// <summary>Gets the explanation shown to callers.</summary>
        public string Message { get; } = message;

        /// <summary>Gets or sets a link to more information.</summary>
        public string? Url { get; set; }
    }
}
#endif