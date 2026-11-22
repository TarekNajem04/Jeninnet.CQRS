//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Transport.Aws;

/// <summary>Shared validation for the SQS/SNS options.</summary>
internal static class AwsOptionsValidator {
    internal static AwsTransportOptions Validate(AwsTransportOptions options) {
        Guard.NotNull(options);

        if(StringCompat.IsNullOrWhiteSpace(options.Region)) {
            throw new ArgumentException("Region must not be empty.", nameof(options));
        }

        if(options.VisibilityTimeout <= TimeSpan.Zero) {
            throw new ArgumentOutOfRangeException(nameof(options), options.VisibilityTimeout, "VisibilityTimeout must be greater than zero.");
        }

        if(options.VisibilityRenewalInterval <= TimeSpan.Zero || options.VisibilityRenewalInterval >= options.VisibilityTimeout) {
            throw new ArgumentOutOfRangeException(nameof(options), options.VisibilityRenewalInterval, "VisibilityRenewalInterval must be positive and shorter than VisibilityTimeout.");
        }

        if(options.MaxNumberOfMessages is < 1 or > 10) {
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxNumberOfMessages, "MaxNumberOfMessages must be between 1 and 10.");
        }

        if(options.PayloadLimitBytes is <= 0) {
            throw new ArgumentOutOfRangeException(nameof(options), options.PayloadLimitBytes, "PayloadLimitBytes must be greater than zero.");
        }

        foreach(var subscription in options.Subscriptions) { // NOSONAR: Explicit loop is clearer and faster than a LINQ pipeline here.
            if(subscription.MessageType is null || subscription.MessageType == typeof(object)) {
                throw new ArgumentException("Every AWS subscription must set a concrete MessageType.", nameof(options));
            }
        }

        return options;
    }
}