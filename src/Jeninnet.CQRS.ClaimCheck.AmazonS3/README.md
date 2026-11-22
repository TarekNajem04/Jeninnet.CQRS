# Jeninnet.CQRS.ClaimCheck.AmazonS3

Amazon S3 claim-check provider for `Jeninnet.CQRS`. Object keys are content-addressed and the
serializer content type is stored on the object.

```csharp
builder.Services.AddCqrsAmazonS3ClaimCheck(options => {
    options.BucketName = "orders-claim-check";
    options.Region = "eu-west-1";
    // options.ServiceUrl = "http://localhost:4566"; // LocalStack
    // options.ForcePathStyle = true;
});
```

Credentials come from the standard AWS SDK chain unless `AccessKey`/`SecretKey` are set. The
bucket must already exist; create it with your infrastructure tooling.

## Retention

`AmazonS3ClaimCheckStore` implements `IClaimCheckStoreMaintenance`:

```csharp
var store = provider.GetRequiredService<IClaimCheckStoreMaintenance>();
var deleted = await store.CleanupExpiredAsync(
    TimeSpan.FromDays(7),
    referencedKeys,
    cancellationToken);
```

Cleanup pages through `ListObjectsV2`, skips every key in `referencedKeys`, and deletes only
objects whose `LastModified` is older than the cutoff. Run it as a scheduled maintenance job; the
provider does not own a timer or a reference registry.

## Sizing and encryption

The core threshold defaults to 64 KiB. Keep it below the smallest transport cap and above your
median message so normal traffic stays inline. S3 applies SSE-S3 to new objects by default; use
SSE-KMS with a customer-managed key and a restrictive bucket policy when compliance requires it.
Block public access, disable ACLs, and grant the runtime role only `s3:GetObject`,
`s3:PutObject`, `s3:DeleteObject`, and `s3:ListBucket` on the claim-check prefix. Use TLS for the
transport.

## Deployment notes

- Prefer an IAM role over static access keys.
- Set a bucket lifecycle rule only for abandoned payloads; the reference-aware cleanup above is
  safer because it will not delete a payload a durable row still needs.
- LocalStack is supported for development; the shared provider contract starts it in CI.
