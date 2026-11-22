//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Validation.Tests;

#region Domain Model
public sealed class CreateClientCommand {
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? Email { get; init; }
    public string? Password { get; init; }
    public int? Age { get; init; }
    public Address? HomeAddress { get; init; }
}

public sealed class Address {
    public string? Street { get; init; }
    public string? City { get; init; }
    public string? PostalCode { get; init; }
}
#endregion

#region Validator Using Your Fluent API
public sealed class CreateClientValidator : ValidatorDefinition<CreateClientCommand> {
    protected override void Configure(ValidatorRuleBuilder<CreateClientCommand> builder) {
        // First name
        builder.RuleFor(x => x.FirstName, nameof(CreateClientCommand.FirstName))
            .NotNull("First name is required.")
            .NotEmpty("First name must not be empty.")
            .Length(2, 50, "First name must be between 2 and 50 characters.");

        // Last name
        builder.RuleFor(x => x.LastName, nameof(CreateClientCommand.LastName))
            .NotNull("Last name is required.")
            .NotEmpty("Last name must not be empty.")
            .Length(2, 50);

        // Email
        builder.RuleFor(x => x.Email, nameof(CreateClientCommand.Email))
            .NotNull("Email is required.")
            .NotEmpty("Email must not be empty.")
            .EmailAddress("Email must be a valid email address.");

        // Password
        builder.RuleFor(x => x.Password, nameof(CreateClientCommand.Password))
            .NotNull("Password is required.")
            .MinimumLength(8, "Password must be at least 8 characters.")
            .Must(p => p?.Any(char.IsDigit) is true, "Password must contain at least one digit.")
            .Must(p => p?.Any(char.IsUpper) is true, "Password must contain at least one uppercase letter.");

        // Age
        builder.RuleFor(x => x.Age, nameof(CreateClientCommand.Age))
            .NotNull("Age is required.")
            .InclusiveBetween(18, 120, "Age must be between 18 and 120.");

        // Nested object: Address
        builder.RuleFor(x => x.HomeAddress, nameof(CreateClientCommand.HomeAddress))
            .NotNull("Address is required.")
            .Must(async (address, ct) => {
                await Task.Delay(10, ct).ConfigureAwait(false);
                return address is not null;
            }, "Address failed async validation.");

        // Nested validation for Address fields
        builder.RuleFor(x => x.HomeAddress, nameof(CreateClientCommand.HomeAddress))
            .Must(address => {
                if(address is null) {
                    return true; // Already handled by NotNull above
                }

                var streetValid = !string.IsNullOrWhiteSpace(address.Street);
                var cityValid = !string.IsNullOrWhiteSpace(address.City);
                var postalValid = !string.IsNullOrWhiteSpace(address.PostalCode);

                return streetValid && cityValid && postalValid;
            }, "Address fields must not be empty.");
    }
}
#endregion
