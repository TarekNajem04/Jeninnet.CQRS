# Jeninnet.CQRS.ClaimCheck.AzureBlob

Azure Blob Storage claim-check provider for `Jeninnet.CQRS`. Payloads are content-addressed and
stored with their serializer content type in the blob headers.

```csharp
builder.Services.AddCqrsAzureBlobClaimCheck(options => {
    options.ConnectionString = builder.Configuration.GetConnectionString("Claims");
    options.ContainerName = "claim-check";
    options.CreateContainerIfMissing = false; // infrastructure owns it in production
});
```

The first write can create the container (`CreateContainerIfMissing`, default `true`). Disable it
when the deployed identity has data-plane access only.

## Retention

`AzureBlobClaimCheckStore` implements `IClaimCheckStoreMaintenance`:

```csharp
var store = provider.GetRequiredService<IClaimCheckStoreMaintenance>();
var deleted = await store.CleanupExpiredAsync(
    TimeSpan.FromDays(7),
    referencedKeys,
    cancellationToken);
```

Cleanup lists blobs, skips every key in `referencedKeys`, and deletes only blobs whose
`LastModified` is older than the cutoff. Run it as a scheduled maintenance job; the provider does
not own a timer or a reference registry.

## Sizing and encryption

The core threshold defaults to 64 KiB. Set it below the smallest transport cap and above your
median message so normal traffic stays inline. Blob names are 64-character lowercase hashes;
content type is the blob's `ContentType` header.

Azure Storage encrypts data at rest with service-managed keys by default. For customer-managed
keys, configure the storage account with Key Vault and rotation, and grant the runtime identity
only the blob data roles it needs. Keep the transport on TLS; encryption at rest is not a
substitute for transport security.

## Deployment notes

- Prefer managed identity over the connection string shown above.
- Use a dedicated container and a lifecycle/immutability policy that does not delete claim blobs
  before your maintenance job has had a chance to run.
- Azurite is supported for development; the shared provider contract starts it in CI.
