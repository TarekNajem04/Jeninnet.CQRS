//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tenancy;

/// <summary>
/// Async-local <see cref="ITenantContext"/>. Registered as a singleton; the mutable state is the
/// <see cref="AsyncLocal{T}"/> value, so concurrent requests never see each other's tenant.
/// </summary>
public sealed class AsyncLocalTenantContext : ITenantContext {
    private readonly AsyncLocal<string?> _current = new();

    /// <inheritdoc />
    public string? Current => _current.Value;

    /// <inheritdoc />
    public IDisposable BeginScope(string tenantId) {
        Guard.NotNullOrWhiteSpace(tenantId);

        var previous = _current.Value;
        _current.Value = tenantId.Trim();

        return new Scope(this, previous);
    }

    private sealed class Scope(AsyncLocalTenantContext owner, string? previous) : IDisposable {
        private readonly AsyncLocalTenantContext _owner = owner;
        private readonly string? _previous = previous;
        private bool _disposed;

        public void Dispose() {
            if(_disposed) {
                return;
            }

            _disposed = true;
            _owner._current.Value = _previous;
        }
    }
}
