//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.ClaimCheck.AzureBlob;

/// <summary>Configuration for the Azure Blob Storage claim-check provider.</summary>
public sealed class AzureBlobClaimCheckOptions {
    /// <summary>
    /// Gets or sets the Storage account connection string. Prefer a managed identity or a secret
    /// store in production; the connection string is the shortest path for local Azurite runs.
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Gets or sets the blob container that holds claim payloads.</summary>
    public string ContainerName { get; set; } = "claim-check";

    /// <summary>
    /// Gets or sets whether the first write creates the container. Defaults to <see langword="true"/>.
    /// Set to <see langword="false"/> when infrastructure owns the container and the runtime
    /// identity has data-plane access only.
    /// </summary>
    public bool CreateContainerIfMissing { get; set; } = true;
}
