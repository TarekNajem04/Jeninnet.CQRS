//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Validation.Tests;

public record RegisterUserRequest(string Email, string Password, string? CardNumber);

public sealed class RegisterUserValidator : ValidatorDefinition<RegisterUserRequest> {
    protected override void Configure(ValidatorRuleBuilder<RegisterUserRequest> builder) {
        builder.RuleFor(static x => x.Email, nameof(RegisterUserRequest.Email))
               .NotEmpty("Email is required.")
               .EmailAddress("Email format is invalid.");

        builder.RuleFor(static x => x.Password, nameof(RegisterUserRequest.Password))
               .NotEmpty("Password is required.")
               .MinimumLength(8, "Password must be at least 8 characters.");

        builder.RuleFor(static x => x.CardNumber, nameof(RegisterUserRequest.CardNumber))
               .CreditCard("Card number is invalid.");
    }
}