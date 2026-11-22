//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

namespace Jeninnet.CQRS.Sample.NetFramework;

internal static class Program {
    private static async Task<int> Main() {
        var services = new ServiceCollection();
        services.AddSingleton<ICqrsBroker, CqrsBroker>();
        services.AddSingleton<ICommandDispatcher, CommandDispatcher>();
        services.AddSingleton<IQueryDispatcher, QueryDispatcher>();
        services.AddTransient<ICommandHandler<NetFrameworkCommand, int>, NetFrameworkCommandHandler>();
        services.AddTransient<IQueryHandler<NetFrameworkQuery, string>, NetFrameworkQueryHandler>();

        using var provider = services.BuildServiceProvider();
        var broker = provider.GetRequiredService<ICqrsBroker>();

        var command = await broker.DispatchAsync<NetFrameworkCommand, int>(new NetFrameworkCommand(21)).ConfigureAwait(false);
        var query = await broker.DispatchAsync<NetFrameworkQuery, string>(new NetFrameworkQuery("net462")).ConfigureAwait(false);

        Console.WriteLine($"net462 dispatch: command={command}; query={query}");

        return command is 42 && string.Equals(query, "net462", StringComparison.Ordinal) ? 0 : 1;
    }
}

/// <summary>A command consumed by the net462 smoke.</summary>
/// <param name="Value">The value to double.</param>
public sealed record NetFrameworkCommand(int Value) : ICommand<int>;

/// <summary>Handles <see cref="NetFrameworkCommand"/>.</summary>
public sealed class NetFrameworkCommandHandler : ICommandHandler<NetFrameworkCommand, int> {
    /// <inheritdoc />
    public Task<int> HandleAsync(NetFrameworkCommand command, CancellationToken cancellationToken) =>
        Task.FromResult(command.Value * 2);
}

/// <summary>A query consumed by the net462 smoke.</summary>
/// <param name="Value">The value to return.</param>
public sealed record NetFrameworkQuery(string Value) : IQuery<string>;

/// <summary>Handles <see cref="NetFrameworkQuery"/>.</summary>
public sealed class NetFrameworkQueryHandler : IQueryHandler<NetFrameworkQuery, string> {
    /// <inheritdoc />
    public Task<string> HandleAsync(NetFrameworkQuery query, CancellationToken cancellationToken) =>
        Task.FromResult(query.Value);
}