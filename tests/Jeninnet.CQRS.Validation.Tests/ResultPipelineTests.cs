//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Validation.Tests;

/// <summary>
/// Characterization tests for CQRS-12: validation failures become result values
/// (ErrorOr / FluentResults) instead of throwing, and the handler is skipped.
/// </summary>
[TestClass]
public class ResultPipelineTests {
    private static ValidationBehavior<TCommand, TResponse> Behavior<TCommand, TResponse>(
        ISyncValidator<TCommand> validator,
        CqrsValidationOptions? options = null) =>
        new([validator],
            [],
            NullLogger<ValidationBehavior<TCommand, TResponse>>.Instance,
            options is null ? null : Options.Create(options));

    [TestMethod]
    public async Task ErrorOr_Failure_MapsErrorsAndSkipsHandlerAsync() {
        // Arrange
        var behavior = Behavior<CreateErrorOrWidget, ErrorOr<string>>(
            new FailingValidator<CreateErrorOrWidget>(),
            new CqrsValidationOptions().UseErrorOrResultAdapter<string>());
        var executions = 0;
        Task<ErrorOr<string>> next(CreateErrorOrWidget command, CancellationToken _) {
            executions++;
            return Task.FromResult<ErrorOr<string>>($"created:{command.Name}");
        }

        // Act
        var result = await behavior.HandleAsync(new CreateErrorOrWidget(string.Empty), next, default);

        // Assert: failure as a value, handler never ran, member/code preserved.
        MSTestAssertions.That(() => result.IsError.Should().BeTrue());
        MSTestAssertions.That(() => result.Errors.Should().HaveCount(2));
        MSTestAssertions.That(() => executions.Should().Be(0));

        var nameError = result.Errors.Single(static error => error.Code == "NAME_REQUIRED");
        MSTestAssertions.That(() => nameError.Type.Should().Be(ErrorType.Validation));
        MSTestAssertions.That(() => nameError.Description.Should().Be("Name is required."));
        MSTestAssertions.That(() => nameError.Metadata!["memberName"].Should().Be("Name"));
        MSTestAssertions.That(() => nameError.Metadata!["severity"].Should().Be("Error"));
        MSTestAssertions.That(() => nameError.Metadata!["errorCode"].Should().Be("NAME_REQUIRED"));

        var emailError = result.Errors.Single(static error => error.Code == "EMAIL_INVALID");
        MSTestAssertions.That(() => emailError.Metadata!["memberName"].Should().Be("Email"));
        MSTestAssertions.That(() => emailError.Metadata!["severity"].Should().Be("Warning"));
    }

    [TestMethod]
    public async Task ErrorOr_Success_CallsHandlerAsync() {
        // Arrange
        var behavior = Behavior<CreateErrorOrWidget, ErrorOr<string>>(
            new PassingValidator<CreateErrorOrWidget>(),
            new CqrsValidationOptions().UseErrorOrResultAdapter<string>());
        var executions = 0;
        Task<ErrorOr<string>> next(CreateErrorOrWidget command, CancellationToken _) {
            executions++;
            return Task.FromResult<ErrorOr<string>>($"created:{command.Name}");
        }

        // Act
        var result = await behavior.HandleAsync(new CreateErrorOrWidget("order-1"), next, default);

        // Assert
        MSTestAssertions.That(() => result.IsError.Should().BeFalse());
        MSTestAssertions.That(() => result.Value.Should().Be("created:order-1"));
        MSTestAssertions.That(() => executions.Should().Be(1));
    }

    [TestMethod]
    public async Task FluentResults_Failure_MapsMetadataAndSkipsHandlerAsync() {
        // Arrange
        var behavior = Behavior<CreateFluentWidget, FluentResults.Result<string>>(
            new FailingValidator<CreateFluentWidget>(),
            new CqrsValidationOptions().UseFluentResultsAdapter<string>());
        var executions = 0;
        Task<FluentResults.Result<string>> next(CreateFluentWidget command, CancellationToken _) {
            executions++;
            return Task.FromResult(FluentResults.Result.Ok($"created:{command.Name}"));
        }

        // Act
        var result = await behavior.HandleAsync(new CreateFluentWidget(string.Empty), next, default);

        // Assert
        MSTestAssertions.That(() => result.IsFailed.Should().BeTrue());
        MSTestAssertions.That(() => result.Errors.Should().HaveCount(2));
        MSTestAssertions.That(() => executions.Should().Be(0));

        var nameError = result.Errors.Single(static error => Equals(error.Metadata["errorCode"], "NAME_REQUIRED"));
        MSTestAssertions.That(() => nameError.Message.Should().Be("Name is required."));
        MSTestAssertions.That(() => nameError.Metadata["memberName"].Should().Be("Name"));
        MSTestAssertions.That(() => nameError.Metadata["severity"].Should().Be("Error"));

        var emailError = result.Errors.Single(static error => Equals(error.Metadata["errorCode"], "EMAIL_INVALID"));
        MSTestAssertions.That(() => emailError.Metadata["memberName"].Should().Be("Email"));
        MSTestAssertions.That(() => emailError.Metadata["severity"].Should().Be("Warning"));
    }

    [TestMethod]
    public async Task FluentResults_Success_CallsHandlerAsync() {
        // Arrange
        var behavior = Behavior<CreateFluentWidget, FluentResults.Result<string>>(
            new PassingValidator<CreateFluentWidget>(),
            new CqrsValidationOptions().UseFluentResultsAdapter<string>());
        var executions = 0;
        Task<FluentResults.Result<string>> next(CreateFluentWidget command, CancellationToken _) {
            executions++;
            return Task.FromResult(FluentResults.Result.Ok($"created:{command.Name}"));
        }

        // Act
        var result = await behavior.HandleAsync(new CreateFluentWidget("order-1"), next, default);

        // Assert
        MSTestAssertions.That(() => result.IsSuccess.Should().BeTrue());
        MSTestAssertions.That(() => result.Value.Should().Be("created:order-1"));
        MSTestAssertions.That(() => executions.Should().Be(1));
    }

    [TestMethod]
    public async Task CSharpFunctionalExtensions_Failure_MapsAllErrorsAndSkipsHandlerAsync() {
        // Arrange
        var behavior = Behavior<CreateCSharpWidget, CSharpFunctionalExtensions.Result<string>>(
            new FailingValidator<CreateCSharpWidget>(),
            new CqrsValidationOptions().UseCSharpFunctionalExtensionsResultAdapter<string>());
        var executions = 0;
        Task<CSharpFunctionalExtensions.Result<string>> next(CreateCSharpWidget command, CancellationToken _) {
            executions++;
            return Task.FromResult(CSharpFunctionalExtensions.Result.Success($"created:{command.Name}"));
        }

        // Act
        var result = await behavior.HandleAsync(new CreateCSharpWidget(string.Empty), next, default);

        // Assert: Result<T> has one string error, so every validation detail must survive in it.
        MSTestAssertions.That(() => result.IsFailure.Should().BeTrue());
        MSTestAssertions.That(() => executions.Should().Be(0));
        MSTestAssertions.That(() => result.Error.Should().Contain(nameof(CreateCSharpWidget.Name)));
        MSTestAssertions.That(() => result.Error.Should().Contain("NAME_REQUIRED"));
        MSTestAssertions.That(() => result.Error.Should().Contain("Name is required."));
        MSTestAssertions.That(() => result.Error.Should().Contain("Email"));
        MSTestAssertions.That(() => result.Error.Should().Contain("[Warning]"));
        MSTestAssertions.That(() => result.Error.Should().Contain("EMAIL_INVALID"));
    }

    [TestMethod]
    public async Task CSharpFunctionalExtensions_Success_CallsHandlerAsync() {
        // Arrange
        var behavior = Behavior<CreateCSharpWidget, CSharpFunctionalExtensions.Result<string>>(
            new PassingValidator<CreateCSharpWidget>(),
            new CqrsValidationOptions().UseCSharpFunctionalExtensionsResultAdapter<string>());
        var executions = 0;
        Task<CSharpFunctionalExtensions.Result<string>> next(CreateCSharpWidget command, CancellationToken _) {
            executions++;
            return Task.FromResult(CSharpFunctionalExtensions.Result.Success($"created:{command.Name}"));
        }

        // Act
        var result = await behavior.HandleAsync(new CreateCSharpWidget("order-1"), next, default);

        // Assert
        MSTestAssertions.That(() => result.IsSuccess.Should().BeTrue());
        MSTestAssertions.That(() => result.Value.Should().Be("created:order-1"));
        MSTestAssertions.That(() => executions.Should().Be(1));
    }

    [TestMethod]
    public async Task ResultMode_WithoutMapper_ThrowsAsync() {
        // Arrange: Result strategy but no adapter for this response type.
        var behavior = Behavior<CreateErrorOrWidget, ErrorOr<string>>(
            new FailingValidator<CreateErrorOrWidget>(),
            new CqrsValidationOptions { FailureStrategy = CqrsValidationFailureStrategy.Result });
        static Task<ErrorOr<string>> next(CreateErrorOrWidget _, CancellationToken __) =>
            Task.FromResult<ErrorOr<string>>("unreachable");

        // Act
        var action = () => behavior.HandleAsync(new CreateErrorOrWidget(string.Empty), next, default);

        // Assert: the contract is explicit instead of fabricating a response.
        await MSTestAssertions.ThatAsync(() => action.Should().ThrowAsync<InvalidOperationException>());
    }

    [TestMethod]
    public async Task ThrowMode_StillThrowsAsync() {
        // Arrange: the default stays exceptions-for-control-flow.
        var behavior = Behavior<CreateErrorOrWidget, ErrorOr<string>>(
            new FailingValidator<CreateErrorOrWidget>());
        static Task<ErrorOr<string>> next(CreateErrorOrWidget _, CancellationToken __) =>
            Task.FromResult<ErrorOr<string>>("unreachable");

        // Act
        var action = () => behavior.HandleAsync(new CreateErrorOrWidget(string.Empty), next, default);

        // Assert
        await MSTestAssertions.ThatAsync(() => action.Should().ThrowAsync<ValidationException>());
    }

    private sealed record CreateErrorOrWidget(string Name) : ICommand<ErrorOr<string>>;

    private sealed record CreateFluentWidget(string Name) : ICommand<FluentResults.Result<string>>;

    private sealed record CreateCSharpWidget(string Name) : ICommand<CSharpFunctionalExtensions.Result<string>>;

    private sealed class FailingValidator<T> : ISyncValidator<T> {
        public ValidationResult Validate(T instance) => ValidationResult.FromErrors([
                new ValidationError("Name", "Name is required.", "NAME_REQUIRED"),
                new ValidationError("Email", "Email is invalid.", "EMAIL_INVALID", severity: ValidationErrorSeverity.Warning),
            ]);

        public ValidationResult Validate(T instance, IEnumerable<string> ruleSets) => Validate(instance);
    }

    private sealed class PassingValidator<T> : ISyncValidator<T> {
        public ValidationResult Validate(T instance) => ValidationResult.Success;

        public ValidationResult Validate(T instance, IEnumerable<string> ruleSets) => ValidationResult.Success;
    }
}
