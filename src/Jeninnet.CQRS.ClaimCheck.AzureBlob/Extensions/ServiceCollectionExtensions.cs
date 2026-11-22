//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.ClaimCheck.AzureBlob.Extensions;

/// <summary>Registration for the Azure Blob Storage claim-check provider.</summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the Azure Blob claim-check store and replaces any earlier claim-check registration.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional provider configuration.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsAzureBlobClaimCheck(
        this IServiceCollection services,
        Action<AzureBlobClaimCheckOptions>? configure = null) {
        Guard.NotNull(services);

        services.AddCqrsClaimCheck();
        services.AddOptions<AzureBlobClaimCheckOptions>();

        if(configure is not null) {
            services.Configure(configure);
        }

        services.AddSingleton(provider => new AzureBlobClaimCheckStore(provider.GetRequiredService<IOptions<AzureBlobClaimCheckOptions>>()));
        services.AddSingleton<IClaimCheckStore>(provider => provider.GetRequiredService<AzureBlobClaimCheckStore>());
        services.AddSingleton<IClaimCheckStoreMaintenance>(provider => provider.GetRequiredService<AzureBlobClaimCheckStore>());

        return services;
    }
}