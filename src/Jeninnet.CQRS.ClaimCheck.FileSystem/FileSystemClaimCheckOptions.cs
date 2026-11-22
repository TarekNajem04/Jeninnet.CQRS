//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.ClaimCheck.FileSystem;

/// <summary>Configuration for the file-system claim-check provider.</summary>
public sealed class FileSystemClaimCheckOptions {
    /// <summary>
    /// Gets or sets the absolute or relative directory that holds claim files. The directory is
    /// created on the first write. Use a volume that survives the process, not a container's
    /// ephemeral layer.
    /// </summary>
    public string RootPath { get; set; } = "claim-check";
}
