//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Jobs;

/// <summary>
/// Executes a serialized job payload without reflection: each registration closes the job and
/// handler types at <c>AddJobHandler&lt;TJob, THandler&gt;()</c>.
/// </summary>
public interface IJobHandlerInvoker {
    /// <summary>Gets the job payload type.</summary>
    Type JobType { get; }

    /// <summary>Gets the assembly-qualified job type name stored with every submitted job.</summary>
    string JobTypeName { get; }

    /// <summary>Deserializes the payload, resolves the handler from <paramref name="services"/>, and runs it.</summary>
    /// <param name="services">The job execution scope.</param>
    /// <param name="serializer">The payload serializer.</param>
    /// <param name="payload">The serialized job payload.</param>
    /// <param name="progress">The progress reporter.</param>
    /// <param name="cancellationToken">Cancels the handler.</param>
    Task ExecuteAsync(
        IServiceProvider services,
        IMessageSerializer serializer,
        ReadOnlyMemory<byte> payload,
        IJobProgress progress,
        CancellationToken cancellationToken);
}

/// <summary>
/// Immutable registry of job handlers. Every job type must have exactly one handler; duplicates
/// fail at host startup.
/// </summary>
public sealed class JobHandlerRegistry {
    private readonly Dictionary<Type, IJobHandlerInvoker> _byType;
    private readonly Dictionary<string, IJobHandlerInvoker> _byName;

    /// <summary>Initializes the registry from the registered handlers.</summary>
    /// <param name="handlers">The configured handler invokers.</param>
    /// <exception cref="InvalidOperationException">A job type has more than one handler.</exception>
    public JobHandlerRegistry(IEnumerable<IJobHandlerInvoker> handlers) {
        var registered = (handlers ?? throw new ArgumentNullException(nameof(handlers))).ToArray();
        _byType = new Dictionary<Type, IJobHandlerInvoker>();
        _byName = new Dictionary<string, IJobHandlerInvoker>(StringComparer.Ordinal);

        foreach(var handler in registered) {
            if(_byType.ContainsKey(handler.JobType)) {
                throw new InvalidOperationException(
                    $"Job type '{handler.JobType.FullName}' has more than one registered handler.");
            }

            if(_byName.ContainsKey(handler.JobTypeName)) {
                throw new InvalidOperationException(
                    $"Job type name '{handler.JobTypeName}' has more than one registered handler.");
            }

            _byType.Add(handler.JobType, handler);
            _byName.Add(handler.JobTypeName, handler);
        }
    }

    /// <summary>Resolves the handler for a payload type.</summary>
    /// <param name="jobType">The job payload type.</param>
    /// <returns>The invoker.</returns>
    /// <exception cref="InvalidOperationException">No handler is registered for the type.</exception>
    public IJobHandlerInvoker GetRequired(Type jobType) {
        Guard.NotNull(jobType);

        return _byType.TryGetValue(jobType, out var handler)
            ? handler
            : throw new InvalidOperationException($"No job handler is registered for '{jobType.FullName}'.");
    }

    /// <summary>Resolves the handler for a stored job type name.</summary>
    /// <param name="jobTypeName">The assembly-qualified, full, or simple job type name.</param>
    /// <returns>The invoker.</returns>
    /// <exception cref="InvalidOperationException">No handler is registered for the name.</exception>
    public IJobHandlerInvoker GetRequired(string jobTypeName) {
        Guard.NotNullOrWhiteSpace(jobTypeName);

        return _byName.TryGetValue(jobTypeName, out var handler)
            ? handler
            : throw new InvalidOperationException($"No job handler is registered for job type '{jobTypeName}'.");
    }
}

/// <summary>Closed generic invoker registered by <c>AddJobHandler&lt;TJob, THandler&gt;()</c>.</summary>
internal sealed class JobHandlerInvoker<TJob, THandler> : IJobHandlerInvoker
    where TJob : class
    where THandler : class, IJobHandler<TJob> {
    /// <inheritdoc />
    public Type JobType => typeof(TJob);

    /// <inheritdoc />
    public string JobTypeName => typeof(TJob).AssemblyQualifiedName ?? typeof(TJob).FullName ?? typeof(TJob).Name;

    /// <inheritdoc />
    public Task ExecuteAsync(
        IServiceProvider services,
        IMessageSerializer serializer,
        ReadOnlyMemory<byte> payload,
        IJobProgress progress,
        CancellationToken cancellationToken) {
        Guard.NotNull(services);
        Guard.NotNull(serializer);
        Guard.NotNull(progress);

        var job = serializer.Deserialize(payload.Span, typeof(TJob)) as TJob
            ?? throw new InvalidOperationException(
                $"The stored payload for job type '{typeof(TJob).FullName}' did not deserialize.");
        var handler = (IJobHandler<TJob>)services.GetRequiredService<THandler>();

        return handler.ExecuteAsync(job, progress, cancellationToken);
    }
}