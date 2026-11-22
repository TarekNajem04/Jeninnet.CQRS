//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

namespace Jeninnet.CQRS.Transport.Contracts;

/// <summary>
/// Docker availability probe shared by container-backed transport tests. A short retry avoids
/// marking a suite inconclusive while Docker Desktop is busy starting another container. The
/// probe also requires the Linux-container engine: every transport image is Linux-only, and a
/// Windows-container daemon reports <c>docker info</c> as healthy but cannot start them.
/// </summary>
public static class DockerAvailability {
    /// <summary>Returns true when Docker reports a running Linux-container engine.</summary>
    /// <param name="cancellationToken">Cancels the probe.</param>
    public static async Task<bool> IsAvailableAsync(CancellationToken cancellationToken) {
        for(var attempt = 0; attempt < 3; attempt++) {
            Process? process = null;

            try {
                var startInfo = new ProcessStartInfo("docker") {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                startInfo.ArgumentList.Add("info");
                startInfo.ArgumentList.Add("--format");
                startInfo.ArgumentList.Add("{{.OSType}}");

                process = Process.Start(startInfo);

                if(process is null) {
                    return false;
                }

                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                await process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);

                if(process.ExitCode is 0 && output.Trim().Equals("linux", StringComparison.OrdinalIgnoreCase)) {
                    return true;
                }
            }
            catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested) {
                return false;
            }
            catch {
                try {
                    process?.Kill(entireProcessTree: true);
                }
                catch {
                    // No process to clean up.
                }
            }
            finally {
                process?.Dispose();
            }

            if(attempt < 2) {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
            }
        }

        return false;
    }
}