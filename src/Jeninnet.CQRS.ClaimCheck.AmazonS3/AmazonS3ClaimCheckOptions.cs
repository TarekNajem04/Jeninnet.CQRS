//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.ClaimCheck.AmazonS3;

/// <summary>Configuration for the Amazon S3 claim-check provider.</summary>
public sealed class AmazonS3ClaimCheckOptions {
    /// <summary>Gets or sets the bucket that holds claim payloads. The bucket must already exist.</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an S3-compatible endpoint, such as LocalStack. Leave <see langword="null"/>
    /// for AWS.
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>Gets or sets the AWS region system name. Defaults to <c>us-east-1</c>.</summary>
    public string Region { get; set; } = "us-east-1";

    /// <summary>Gets or sets an explicit access key; leave empty to use the standard AWS chain.</summary>
    public string? AccessKey { get; set; }

    /// <summary>Gets or sets an explicit secret key; leave empty to use the standard AWS chain.</summary>
    public string? SecretKey { get; set; }

    /// <summary>
    /// Gets or sets whether path-style addressing is used. LocalStack and some S3-compatible
    /// services require <see langword="true"/>.
    /// </summary>
    public bool ForcePathStyle { get; set; }
}