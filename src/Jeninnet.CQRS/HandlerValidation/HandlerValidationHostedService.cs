//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.HandlerValidation;

/// <summary>
/// Runs handler validation once at startup and fails the host when findings are fatal.
/// </summary>
public sealed class HandlerValidationHostedService(IServiceProvider provider, IOptions<HandlerValidationOptions> options) : IHostedService {
    private readonly IServiceProvider _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    private readonly HandlerValidationOptions _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) {
        var report = _provider.GetRequiredService<IHandlerValidator>().Validate();

        if(!report.IsValid && _options.FailOnStartup) {
            throw new CqrsHandlerValidationException(report.Findings);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}