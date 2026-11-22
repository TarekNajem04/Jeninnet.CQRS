//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Polyfills;

/// <summary>Process identity without the netstandard2.0/Environment.ProcessId API gap.</summary>
internal static class ProcessIdentity {
    /// <summary>Gets the current process id.</summary>
    public static int Id {
        get {
#if NETSTANDARD2_0 || NET462
            return System.Diagnostics.Process.GetCurrentProcess().Id;
#else
            return Environment.ProcessId;
#endif
        }
    }
}