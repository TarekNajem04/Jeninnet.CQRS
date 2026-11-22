//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
#if NETSTANDARD2_0 || NET462
namespace System.Threading;

/// <summary>netstandard2.0 implementation of the .NET 9 lock type.</summary>
internal sealed class Lock {
    private readonly object _gate = new();

    /// <summary>Enters the lock and returns a scope that exits it.</summary>
    public Scope EnterScope() {
        Monitor.Enter(_gate);

        return new Scope(_gate);
    }

    /// <summary>Releases the lock when disposed.</summary>
    public ref struct Scope {
        private object? _gate;

        internal Scope(object gate) {
            _gate = gate;
        }

        /// <summary>Exits the lock.</summary>
        public void Dispose() {
            var gate = _gate;

            if(gate is not null) {
                _gate = null;
                Monitor.Exit(gate);
            }
        }
    }
}
#endif