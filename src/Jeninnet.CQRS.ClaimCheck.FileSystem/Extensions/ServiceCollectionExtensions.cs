//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.ClaimCheck.FileSystem.Extensions;

/// <summary>Registration for the file-system claim-check provider.</summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the file-system claim-check store and replaces any earlier claim-check registration.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional provider configuration.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsFileSystemClaimCheck(
        this IServiceCollection services,
        Action<FileSystemClaimCheckOptions>? configure = null) {
        Guard.NotNull(services);

        services.AddCqrsClaimCheck();
        services.AddOptions<FileSystemClaimCheckOptions>();

        if(configure is not null) {
            services.Configure(configure);
        }

        services.AddSingleton(provider => new FileSystemClaimCheckStore(provider.GetRequiredService<IOptions<FileSystemClaimCheckOptions>>()));
        services.AddSingleton<IClaimCheckStore>(provider => provider.GetRequiredService<FileSystemClaimCheckStore>());
        services.AddSingleton<IClaimCheckStoreMaintenance>(provider => provider.GetRequiredService<FileSystemClaimCheckStore>());

        return services;
    }
}
