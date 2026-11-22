//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

namespace Jeninnet.CQRS.Cascading;

/// <summary>
/// Executes cascaded follow-ups after the command pipeline (including transactions) completes,
/// with bounded depth and cycle detection.
/// </summary>
internal static class CascadeRunner {
    private static readonly AsyncLocal<int> Depth = new();
    private static readonly AsyncLocal<List<Type>?> Chain = new();

    internal static async Task RunAsync(object? result, Type rootMessageType, IServiceProvider provider, CancellationToken cancellationToken) {
        if(result is not ICascadingMessages cascades || cascades.Steps.Count is 0) {
            return;
        }

        Guard.NotNull(provider);
        Guard.NotNull(rootMessageType);

        var options = provider.GetService<CascadeOptions>() ?? new CascadeOptions();
        var depth = Depth.Value + 1;
        var chain = Chain.Value ??= [];

        if(depth is 1) {
            chain.Add(rootMessageType);
        }

        var broker = provider.GetRequiredService<ICqrsBroker>();
        Depth.Value = depth;

        try {
            foreach(var step in cascades.Steps) {
                if(depth >= options.MaxDepth) {
                    throw new InvalidOperationException($"Cascade depth exceeded the configured maximum of {options.MaxDepth}.");
                }

                if(chain.Contains(step.MessageType)) {
                    throw new InvalidOperationException($"Cascade cycle detected: {Describe(chain)} -> {step.MessageType.Name}.");
                }

                chain.Add(step.MessageType);

                try {
                    await step.Dispatch(broker, cancellationToken).ConfigureAwait(false);
                }
                finally {
                    chain.RemoveAt(chain.Count - 1);
                }
            }
        }
        finally {
            Depth.Value = depth - 1;

            if(depth is 1) {
                chain.Remove(rootMessageType);
                Chain.Value = null;
            }
        }
    }

    private static string Describe(List<Type> chain) {
        var parts = new string[chain.Count];

        for(var i = 0; i < chain.Count; i++) {
            parts[i] = chain[i].Name;
        }

        return string.Join(" -> ", parts);
    }
}