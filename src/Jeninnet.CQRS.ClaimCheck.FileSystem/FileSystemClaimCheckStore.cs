//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.ClaimCheck.FileSystem;

/// <summary>
/// File-system <see cref="IClaimCheckStore"/>. Files are content-addressed by
/// <see cref="ClaimCheckKey"/>, written atomically through a temporary file, and framed with a
/// four-byte content-type length so the serializer metadata survives a restart.
/// </summary>
/// <remarks>
/// This provider is intended for a single host with a durable local volume. A shared network
/// volume works only when all writers agree on file locking semantics. For multi-host
/// deployments, use the Azure Blob or Amazon S3 provider.
/// </remarks>
public sealed class FileSystemClaimCheckStore : IClaimCheckStore, IClaimCheckStoreMaintenance {
    private const string FileExtension = ".claim";
    private const int MaxContentTypeBytes = 8 * 1024;
    private readonly string _rootPath;

    /// <summary>Initializes a store over <paramref name="rootPath"/>.</summary>
    /// <param name="rootPath">The directory that holds claim files.</param>
    public FileSystemClaimCheckStore(string rootPath) {
        Guard.NotNullOrWhiteSpace(rootPath);

        _rootPath = Path.GetFullPath(rootPath);
    }

    /// <summary>Initializes a store from configured options.</summary>
    /// <param name="options">The provider options.</param>
    public FileSystemClaimCheckStore(IOptions<FileSystemClaimCheckOptions> options)
        : this((options ?? throw new ArgumentNullException(nameof(options))).Value.RootPath) {
    }

    /// <summary>Gets the absolute directory that holds claim files.</summary>
    public string RootPath => _rootPath;

    /// <inheritdoc />
    public async Task<string> PutAsync(ReadOnlyMemory<byte> payload, string contentType, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(contentType);

        var key = ClaimCheckKey.Compute(payload, contentType);
        var path = GetPath(key);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var contentTypeBytes = Encoding.UTF8.GetBytes(contentType);
        var lengthBytes = BitConverter.GetBytes(contentTypeBytes.Length);

        if(!BitConverter.IsLittleEndian) {
            Array.Reverse(lengthBytes);
        }

        Directory.CreateDirectory(_rootPath);

        try {
            using(var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true)) {
                await WriteAsync(stream, lengthBytes, cancellationToken).ConfigureAwait(false);
                await WriteAsync(stream, contentTypeBytes, cancellationToken).ConfigureAwait(false);

                if(payload.Length > 0) {
                    await WriteAsync(stream, payload.ToArray(), cancellationToken).ConfigureAwait(false);
                }

                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            try {
                File.Move(temporaryPath, path);
            }
            catch(IOException) when(File.Exists(path)) {
                // Another writer stored the same content-addressed payload first.
                File.Delete(temporaryPath);
            }
        }
        catch {
            TryDelete(temporaryPath);

            throw;
        }

        return key;
    }

    /// <inheritdoc />
    public async Task<ClaimCheckPayload?> GetAsync(string key, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);

        FileStream stream;

        try {
            stream = new FileStream(GetPath(key), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);
        }
        catch(FileNotFoundException) {
            return null;
        }
        catch(DirectoryNotFoundException) {
            return null;
        }

        using(stream) {
            var lengthBytes = new byte[4];

            if(!await ReadExactlyAsync(stream, lengthBytes, cancellationToken).ConfigureAwait(false)) {
                throw new InvalidDataException($"Claim-check file '{key}' is truncated.");
            }

            if(!BitConverter.IsLittleEndian) {
                Array.Reverse(lengthBytes);
            }

            var contentTypeLength = BitConverter.ToInt32(lengthBytes, 0);

            if(contentTypeLength is < 0 or > MaxContentTypeBytes) {
                throw new InvalidDataException($"Claim-check file '{key}' has an invalid content-type length.");
            }

            var contentTypeBytes = new byte[contentTypeLength];

            if(!await ReadExactlyAsync(stream, contentTypeBytes, cancellationToken).ConfigureAwait(false)) {
                throw new InvalidDataException($"Claim-check file '{key}' is truncated.");
            }

            using var payload = new MemoryStream();
            await stream.CopyToAsync(payload, 81920, cancellationToken).ConfigureAwait(false);

            return new ClaimCheckPayload(payload.ToArray(), Encoding.UTF8.GetString(contentTypeBytes));
        }
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key, CancellationToken cancellationToken = default) {
        Guard.NotNullOrWhiteSpace(key);

        TryDelete(GetPath(key));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<int> CountAsync(CancellationToken cancellationToken = default) {
        if(!Directory.Exists(_rootPath)) {
            return Task.FromResult(0);
        }

        var count = Directory.EnumerateFiles(_rootPath, "*" + FileExtension, SearchOption.TopDirectoryOnly).Count();

        return Task.FromResult(count);
    }

    /// <inheritdoc />
    public Task<int> CleanupAsync(
        DateTimeOffset olderThanUtc,
        IReadOnlyCollection<string> referencedKeys,
        CancellationToken cancellationToken = default) {
        Guard.NotNull(referencedKeys);

        if(!Directory.Exists(_rootPath)) {
            return Task.FromResult(0);
        }

        var referenced = new HashSet<string>(referencedKeys, StringComparer.Ordinal);
        var deleted = 0;

        foreach(var path in Directory.EnumerateFiles(_rootPath, "*" + FileExtension, SearchOption.TopDirectoryOnly)) {
            cancellationToken.ThrowIfCancellationRequested();

            var key = Path.GetFileNameWithoutExtension(path);

            if(referenced.Contains(key)) {
                continue;
            }

            DateTime lastWriteUtc;

            try {
                lastWriteUtc = File.GetLastWriteTimeUtc(path);
            }
            catch(FileNotFoundException) {
                continue;
            }

            if(lastWriteUtc >= olderThanUtc.UtcDateTime) {
                continue;
            }

            if(TryDelete(path)) {
                deleted++;
            }
        }

        return Task.FromResult(deleted);
    }

    private string GetPath(string key) => Path.Combine(_rootPath, key + FileExtension);

    private static async Task<bool> ReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken) {
        var offset = 0;

        while(offset < buffer.Length) {
            var read = await ReadAsync(stream, buffer, offset, buffer.Length - offset, cancellationToken).ConfigureAwait(false);

            if(read is 0) {
                return false;
            }

            offset += read;
        }

        return true;
    }

    private static async Task WriteAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken) {
#if NETSTANDARD2_0 || NET462
        await stream.WriteAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false);
#else
        await stream.WriteAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
#endif
    }

    private static async Task<int> ReadAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken) {
#if NETSTANDARD2_0 || NET462
        return await stream.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
#else
        return await stream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
#endif
    }

    private static bool TryDelete(string path) {
        try {
            if(!File.Exists(path)) {
                return false;
            }

            File.Delete(path);

            return true;
        }
        catch(FileNotFoundException) {
            return false;
        }
        catch(DirectoryNotFoundException) {
            return false;
        }
    }
}