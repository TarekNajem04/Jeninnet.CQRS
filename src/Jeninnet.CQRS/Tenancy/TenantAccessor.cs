//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tenancy;

/// <summary>
/// Small adapter that resolves the ambient tenant and enforces
/// <see cref="MultiTenancyOptions.RequireTenant"/>. Registered by
/// <c>AddCqrsMultiTenancy</c>; core publishers accept it optionally, so applications without
/// tenancy keep their existing behavior.
/// </summary>
/// <param name="context">The ambient tenant context, or <see langword="null"/>.</param>
/// <param name="options">The tenancy options, or <see langword="null"/>.</param>
public sealed class TenantAccessor(ITenantContext? context = null, IOptions<MultiTenancyOptions>? options = null) {
    private readonly ITenantContext? _context = context;
    private readonly MultiTenancyOptions? _options = options?.Value;

    /// <summary>Gets the current tenant, enforcing strict mode when enabled.</summary>
    public string? Current =>
        _context is null
            ? null
            : _options is null
                ? _context.Current
                : _context.ResolveTenant(_options);

    /// <summary>Resolves an explicit stored tenant first, then the ambient tenant.</summary>
    /// <param name="explicitTenant">The tenant persisted on the message, when available.</param>
    /// <returns>The tenant id, or <see langword="null"/> in optional mode.</returns>
    public string? Resolve(string? explicitTenant) =>
        _context is null
            ? StringCompat.IsNullOrWhiteSpace(explicitTenant) ? null : explicitTenant.Trim()
            : _options is null
                ? StringCompat.IsNullOrWhiteSpace(explicitTenant) ? _context.Current : explicitTenant.Trim()
                : _context.ResolveTenant(_options, explicitTenant);

    /// <summary>Opens a scope for a stored tenant; returns <see langword="null"/> when absent.</summary>
    /// <param name="tenantId">The stored tenant.</param>
    /// <returns>The scope, or <see langword="null"/>.</returns>
    public IDisposable? EnterScope(string? tenantId) => _context.EnterScope(tenantId);
}