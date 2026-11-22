//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Aws;

/// <summary>Builds SQS/SNS clients from the standard AWS credential chain and optional endpoint.</summary>
internal static class AwsClientFactory {
    internal static AmazonSQSClient CreateSqs(AwsTransportOptions options) {
        var config = new AmazonSQSConfig {
            RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region),
        };

        if(!StringCompat.IsNullOrWhiteSpace(options.ServiceUrl)) {
            config.ServiceURL = options.ServiceUrl;
        }

        return new AmazonSQSClient(config);
    }

    internal static AmazonSimpleNotificationServiceClient CreateSns(AwsTransportOptions options) {
        var config = new AmazonSimpleNotificationServiceConfig {
            RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region),
        };

        if(!StringCompat.IsNullOrWhiteSpace(options.ServiceUrl)) {
            config.ServiceURL = options.ServiceUrl;
        }

        return new AmazonSimpleNotificationServiceClient(config);
    }
}