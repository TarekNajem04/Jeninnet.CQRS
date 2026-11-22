# Jeninnet.CQRS.ClaimCheck.FileSystem

File-system claim-check provider for `Jeninnet.CQRS`. Large serialized payloads are written to a
durable local directory and the durable row keeps only the content-addressed key.

```csharp
builder.Services.AddCqrsFileSystemClaimCheck(options =>
    options.RootPath = "/var/lib/orders/claim-check");
```

The directory is created on the first write. Files are framed with their content type, written
through a temporary file, and named by the SHA-256 of the content type and payload. Storing the
same payload twice returns the same key and leaves one file.

## Retention

`FileSystemClaimCheckStore` implements `IClaimCheckStoreMaintenance`:

```csharp
var store = provider.GetRequiredService<IClaimCheckStoreMaintenance>();
var deleted = await store.CleanupExpiredAsync(
    TimeSpan.FromDays(7),
    referencedKeys,
    cancellationToken);
```

`referencedKeys` must contain the claim-check keys still referenced by deferred, outbox,
dead-letter, idempotency, or other durable rows. The provider never deletes a referenced file,
regardless of age. Run cleanup from one host or serialize it; the provider does not coordinate
leases between multiple writers on a network share.

## Sizing and encryption

The core threshold defaults to 64 KiB. Keep it comfortably below the smallest transport payload
cap (SQS is 256 KB) and far above your median message so only genuinely large payloads pay the
storage round trip. The provider stores bytes as-is: use an encrypted volume or OS-level disk
encryption, restrict directory permissions to the service account, and keep the transport on TLS.
The package deliberately does not implement application-level encryption; document the KMS or
volume-encryption decision in your deployment runbook.

## Deployment notes

- Use a persistent volume, not a container's writable layer.
- A shared network volume is supported only when all writers agree on file locking semantics; for
  multi-host deployments prefer the Azure Blob or Amazon S3 provider.
- Back up the directory with the rest of the durable stores so claim references and payloads stay
  consistent.
