//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.ClaimCheck.AmazonS3.Extensions;

/// <summary>Registration for the Amazon S3 claim-check provider.</summary>
public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds the Amazon S3 claim-check store and replaces any earlier claim-check registration.
    /// An existing <see cref="IAmazonS3"/> registration wins, so callers may configure the SDK
    /// client themselves.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add to.</param>
    /// <param name="configure">Optional provider configuration.</param>
    /// <returns>The updated <see cref="IServiceCollection"/> instance.</returns>
    public static IServiceCollection AddCqrsAmazonS3ClaimCheck(
        this IServiceCollection services,
        Action<AmazonS3ClaimCheckOptions>? configure = null) {
        Guard.NotNull(services);

        services.AddCqrsClaimCheck();
        services.AddOptions<AmazonS3ClaimCheckOptions>();

        if(configure is not null) {
            services.Configure(configure);
        }

        services.TryAddSingleton<IAmazonS3>(provider => CreateClient(provider.GetRequiredService<IOptions<AmazonS3ClaimCheckOptions>>().Value));
        services.AddSingleton(provider => {
            var options = provider.GetRequiredService<IOptions<AmazonS3ClaimCheckOptions>>().Value;

            return new AmazonS3ClaimCheckStore(provider.GetRequiredService<IAmazonS3>(), options.BucketName);
        });
        services.AddSingleton<IClaimCheckStore>(provider => provider.GetRequiredService<AmazonS3ClaimCheckStore>());
        services.AddSingleton<IClaimCheckStoreMaintenance>(provider => provider.GetRequiredService<AmazonS3ClaimCheckStore>());

        return services;
    }

    private static AmazonS3Client CreateClient(AmazonS3ClaimCheckOptions options) {
        Guard.NotNull(options);

        var config = new AmazonS3Config {
            ForcePathStyle = options.ForcePathStyle,
        };

        // RegionEndpoint resets ServiceURL in the AWS SDK; set the custom endpoint last.
        if(!StringCompat.IsNullOrWhiteSpace(options.Region)) {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        }

        if(!StringCompat.IsNullOrWhiteSpace(options.ServiceUrl)) {
            config.ServiceURL = options.ServiceUrl;
        }

        return StringCompat.IsNullOrWhiteSpace(options.AccessKey) || StringCompat.IsNullOrWhiteSpace(options.SecretKey)
            ? new AmazonS3Client(config)
            : new AmazonS3Client(new BasicAWSCredentials(options.AccessKey, options.SecretKey), config);
    }
}