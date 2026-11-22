//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
global using Jeninnet.Validation.Attributes;

namespace Jeninnet.CQRS.Validation.Tests;

[Validate]
public partial class User {
    [NotEmpty]
    [Length(5, 20)]
    public string Username { get; set; } = string.Empty;
}

public class CreateUserRequest {
    public Guid Id { get; internal set; }
    public string Email { get; internal set; } = string.Empty;
    public string Password { get; internal set; } = string.Empty;
    public bool IsActive { get; internal set; }
    public string DeactivationReason { get; internal set; } = string.Empty;
    public string ExternalId { get; internal set; } = string.Empty;
    public bool RequiresExternalValidation { get; internal set; }
    public bool IsDeleted { get; internal set; }

    public int Age { get; internal set; }
}

public sealed class CreateUserValidator : ValidatorDefinition<CreateUserRequest> {
    protected override void Configure(ValidatorRuleBuilder<CreateUserRequest> builder) {
        builder.RuleFor(x => x.Email, nameof(CreateUserRequest.Email))
               .NotEmpty("Email is required.");

        builder.RuleFor(x => x.Password, nameof(CreateUserRequest.Password))
               .NotEmpty("Password is required.");
    }
}

public sealed class UserValidator : ValidatorDefinition<CreateUserRequest> {
    protected override void Configure(ValidatorRuleBuilder<CreateUserRequest> builder) {
        // Only validate Email when IsActive is true
        builder.When(u => u.IsActive, b => b.RuleFor(x => x.Email, nameof(CreateUserRequest.Email))
               .NotEmpty("Email is required for active users.")
               .EmailAddress("Email format is invalid."));

        // Only validate DeactivationReason when user is marked as deleted (nullable string)
        builder.When(u => u.IsDeleted, b => b.RuleFor(x => x.DeactivationReason, nameof(CreateUserRequest.DeactivationReason))
               .NotEmpty("Deactivation reason is required for deleted users."));

        // Async condition example (e.g. external status check)
        builder.WhenAsyncRule(
            async (u, token) => {
                // Example: pretend we call an external service
                await Task.Delay(1, token).ConfigureAwait(false);
                return u.RequiresExternalValidation;
            },
            b => b.RuleFor(x => x.ExternalId, nameof(CreateUserRequest.ExternalId))
                  .NotEmpty("ExternalId is required when external validation is required."));

        builder.RuleFor(x => x.Email, nameof(CreateUserRequest.Email))
               .Cascade(CascadeMode.Stop)
               .NotEmpty("Email is required.").EmailAddress("Email format is invalid.");

        builder.RuleFor(x => x.Age, nameof(CreateUserRequest.Age))
               .GreaterThan(0, "Age must be positive.")
               .LessThanOrEqual(130, "Age must be realistic.");

        // Create-specific rules
        builder.RuleSet("Create", b => b.RuleFor(x => x.Password, nameof(CreateUserRequest.Password))
            .NotEmpty("Password is required on creation.")
            .MinimumLength(8));

        // Update-specific rules
        builder.RuleSet("Update", b => b.RuleFor(x => x.Id, nameof(CreateUserRequest.Id))
             .NotEmpty("Id must be provided on update."));
    }
}
