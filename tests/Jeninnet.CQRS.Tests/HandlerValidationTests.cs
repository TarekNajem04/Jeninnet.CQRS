//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// Characterization tests for CQRS-24: startup validation reports captive dependencies,
/// duplicates, and construction failures — and never treats a missing handler as an error.
/// </summary>
[TestClass]
public class HandlerValidationTests {
    private static (IServiceProvider Provider, HandlerValidationOptions Options) Build(Action<IServiceCollection>? configure, Action<HandlerValidationOptions>? options = null) {
        var services = new ServiceCollection();
        configure?.Invoke(services);

        var effective = new HandlerValidationOptions();
        options?.Invoke(effective);

        services.AddCqrsHandlerValidation(o => {
            o.FailOnStartup = effective.FailOnStartup;
            o.CheckCaptiveDependencies = effective.CheckCaptiveDependencies;
            o.CheckDuplicateHandlers = effective.CheckDuplicateHandlers;
        });

        return (services.BuildServiceProvider(), effective);
    }

    [TestMethod]
    public void HealthyRegistrations_Pass() {
        // Arrange
        var (provider, _) = Build(services => {
            services.AddTransient<ICommandHandler<ValidCommand, string>, ValidHandler>();
        });

        // Act
        var report = provider.GetRequiredService<IHandlerValidator>().Validate();

        // Assert
        MSTestAssertions.That(() => report.Findings.Should().BeEmpty());
        MSTestAssertions.That(() => report.IsValid.Should().BeTrue());
    }

    [TestMethod]
    public void SingletonHandlerWithScopedDependency_IsReported() {
        // Arrange
        var (provider, _) = Build(services => {
            services.AddScoped<ScopedDependency>();
            services.AddSingleton<ICommandHandler<CaptureCommand, string>, CaptiveHandler>();
        });

        // Act
        var report = provider.GetRequiredService<IHandlerValidator>().Validate();
        var captive = report.Findings.Single(static finding => finding.Kind is HandlerValidationKind.CaptiveDependency);

        // Assert: both sides are named.
        MSTestAssertions.That(() => captive.Message.Should().Contain(nameof(CaptiveHandler)));
        MSTestAssertions.That(() => captive.Message.Should().Contain(nameof(ScopedDependency)));
    }

    [TestMethod]
    public void DuplicateCommandHandlers_AreReported() {
        // Arrange
        var (provider, _) = Build(services => {
            services.AddTransient<ICommandHandler<CaptureCommand, string>, FirstCaptureHandler>();
            services.AddTransient<ICommandHandler<CaptureCommand, string>, SecondValidHandler>();
        });

        // Act
        var report = provider.GetRequiredService<IHandlerValidator>().Validate();

        // Assert
        var duplicate = report.Findings.Single(static finding => finding.Kind is HandlerValidationKind.DuplicateHandler);
        MSTestAssertions.That(() => duplicate.Message.Should().Contain("2 handlers"));
    }

    [TestMethod]
    public void UnresolvableHandler_IsReported() {
        // Arrange
        var (provider, _) = Build(services => {
            services.AddTransient<ICommandHandler<CaptureCommand, string>, NeedsMissingServiceHandler>();
        });

        // Act
        var report = provider.GetRequiredService<IHandlerValidator>().Validate();

        // Assert
        var finding = report.Findings.Single(static item => item.Kind is HandlerValidationKind.UnresolvableDependency);
        MSTestAssertions.That(() => finding.Message.Should().Contain(nameof(MissingService)));
    }

    [TestMethod]
    public void MissingHandler_IsNotAnError() {
        // Arrange: no handler for any message at all.
        var (provider, _) = Build(services => services.AddSingleton(new object()));

        // Act
        var report = provider.GetRequiredService<IHandlerValidator>().Validate();

        // Assert
        MSTestAssertions.That(() => report.IsValid.Should().BeTrue());
    }

    [TestMethod]
    public async Task Startup_FailsWithAllFindingsAsync() {
        // Arrange: two independent problems at once.
        var (provider, _) = Build(services => {
            services.AddScoped<ScopedDependency>();
            services.AddSingleton<ICommandHandler<CaptureCommand, string>, CaptiveHandler>();
            services.AddTransient<ICommandHandler<CaptureCommand, string>, NeedsMissingServiceHandler>();
        });

        // Act
        var hosted = provider.GetServices<IHostedService>().OfType<HandlerValidationHostedService>();
        var thrown = await CaptureAsync(() => hosted.Single().StartAsync(default));

        // Assert: one exception carries both findings.
        MSTestAssertions.That(() => thrown.Should().BeOfType<CqrsHandlerValidationException>());
        var findings = ((CqrsHandlerValidationException)thrown!).Findings;
        MSTestAssertions.That(() => findings.Count.Should().BeGreaterThanOrEqualTo(2));
        MSTestAssertions.That(() => thrown!.Message.Should().Contain("finding(s)"));
    }

    [TestMethod]
    public async Task FailOnStartupFalse_DoesNotThrowAsync() {
        // Arrange
        var (provider, _) = Build(
            services => services.AddTransient<ICommandHandler<CaptureCommand, string>, NeedsMissingServiceHandler>(),
            options => options.FailOnStartup = false);

        // Act
        var hosted = provider.GetServices<IHostedService>().OfType<HandlerValidationHostedService>().Single();
        var thrown = await CaptureAsync(() => hosted.StartAsync(default));

        // Assert: report-only mode starts the host.
        MSTestAssertions.That(() => thrown.Should().BeNull());
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action) {
        try {
            await action().ConfigureAwait(false);

            return null;
        }
        catch(Exception exception) {
            return exception;
        }
    }

    public sealed record ValidCommand : ICommand<string>;

    public sealed record CaptureCommand : ICommand<string>;

    public sealed class ValidHandler : ICommandHandler<ValidCommand, string> {
        public Task<string> HandleAsync(ValidCommand command, CancellationToken cancellationToken) =>
            Task.FromResult("ok");
    }

    public sealed class SecondValidHandler : ICommandHandler<CaptureCommand, string> {
        public Task<string> HandleAsync(CaptureCommand command, CancellationToken cancellationToken) =>
            Task.FromResult("second");
    }

    public sealed class FirstCaptureHandler : ICommandHandler<CaptureCommand, string> {
        public Task<string> HandleAsync(CaptureCommand command, CancellationToken cancellationToken) =>
            Task.FromResult("first");
    }

    public sealed class ScopedDependency;

    public sealed class MissingService;

    public sealed class CaptiveHandler(ScopedDependency dependency) : ICommandHandler<CaptureCommand, string> {
        public Task<string> HandleAsync(CaptureCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(dependency.ToString()!);
    }

    public sealed class NeedsMissingServiceHandler(MissingService dependency) : ICommandHandler<CaptureCommand, string> {
        public Task<string> HandleAsync(CaptureCommand command, CancellationToken cancellationToken) =>
            Task.FromResult(dependency.ToString()!);
    }
}