//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

// Modes:
//   dispatch (default) — the CQRS-10 generated dispatch proof.
//   stage              — persist two deferred commands, then exit without starting the pump.
//   replay             — start a fresh process, recover the file, and dispatch both commands.
var mode = args.Length > 0 ? args[0] : "dispatch";
var file = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "jeninnet-cqrs-aot-deferred.jsonl");

if(mode is "stage" && File.Exists(file)) {
    File.Delete(file);
}

var services = new ServiceCollection();
services.AddCqrs();
services.AddCqrsDeferredDelivery();
services.AddSingleton<IMessageSerializer, AotJsonMessageSerializer>();
services.AddSingleton<AotReplayProbe>();
services.AddSingleton<IDeferredStore>(provider => new FileDeferredStore(
    file,
    provider.GetRequiredService<IMessageSerializer>(),
    provider.GetRequiredService<IDeferredEnvelopeFactory>()));
await using var provider = services.BuildServiceProvider();

var broker = provider.GetRequiredService<ICqrsBroker>();

if(mode is "stage") {
    var dispatcher = provider.GetRequiredService<IDeferredDispatcher>();
    await dispatcher.DeferAsync(new AotDeferredCommand("AOT"), TimeSpan.Zero, default);
    await dispatcher.DeferAsync<AotDeferredResultCommand, int>(new AotDeferredResultCommand(21), TimeSpan.Zero, default);
    Console.WriteLine("Staged 2 deferred commands.");

    return 0;
}

if(mode is "replay") {
    var dispatcher = provider.GetRequiredService<IDeferredDispatcher>();
    var hosted = (IHostedService)dispatcher;
    var probe = provider.GetRequiredService<AotReplayProbe>();
    await hosted.StartAsync(default);

    try {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await probe.Completion.WaitAsync(timeout.Token);
        Console.WriteLine(probe.Summary);

        return probe.Succeeded ? 0 : 1;
    }
    catch(OperationCanceledException) {
        Console.WriteLine($"Replay timed out. Received: {probe.Summary}");

        return 1;
    }
    finally {
        await hosted.StopAsync(default);
    }
}

var greeting = await broker.DispatchAsync<GreetCommand, string>(new GreetCommand("AOT"), default);
Console.WriteLine(greeting);

var quote = await broker.DispatchAsync<GetQuoteQuery, string>(new GetQuoteQuery(7), default);
Console.WriteLine(quote);

return greeting == "Hello, AOT!" && quote == "Quote #7" ? 0 : 1;

namespace Jeninnet.CQRS.Sample.AotSmoke {
    public sealed record GreetCommand(string Name) : ICommand<string>;

    public sealed class GreetHandler : ICommandHandler<GreetCommand, string> {
        public Task<string> HandleAsync(GreetCommand command, CancellationToken cancellationToken) =>
            Task.FromResult($"Hello, {command.Name}!");
    }

    public sealed record GetQuoteQuery(int Id) : IQuery<string>;

    public sealed class GetQuoteHandler : IQueryHandler<GetQuoteQuery, string> {
        public Task<string> HandleAsync(GetQuoteQuery query, CancellationToken cancellationToken) =>
            Task.FromResult($"Quote #{query.Id}");
    }
}
