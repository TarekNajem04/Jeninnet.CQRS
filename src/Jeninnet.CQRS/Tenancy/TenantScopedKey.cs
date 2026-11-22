//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tenancy;

/// <summary>Default <see cref="ITenantScopedKey"/> over <see cref="ITenantContext"/>.</summary>
/// <param name="context">The ambient tenant. Never <see langword="null"/>.</param>
/// <param name="options">The strict-mode and separator options. Never <see langword="null"/>.</param>
public sealed class TenantScopedKey(ITenantContext context, IOptions<MultiTenancyOptions> options) : ITenantScopedKey {
    private readonly ITenantContext _context = context ?? throw new ArgumentNullException(nameof(context));
    private readonly MultiTenancyOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;

    /// <inheritdoc />
    public string Compose(string key) {
        Guard.NotNull(key);

        var tenant = _context.ResolveTenant(_options);

        return StringCompat.IsNullOrEmpty(tenant) ? key : $"{tenant}{_options.KeySeparator}{key}";
    }
}