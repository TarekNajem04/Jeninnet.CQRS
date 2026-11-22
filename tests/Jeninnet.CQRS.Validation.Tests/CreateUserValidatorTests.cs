//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Validation.Tests;

[TestClass]
public class CreateUserValidatorTests {
    [TestMethod]
    public void Should_Return_Error_When_Email_Is_Missing() {
        var definition = new CreateUserValidator();
        var validator = definition.Build();

        ValidationConfiguration.Configure(options =>
           options with {
               DefaultCascadeMode = CascadeMode.Stop
           });

        var result = validator.Validate(new CreateUserRequest { Email = "", Password = "" });

        MSTestAssertions.That(() => result.IsValid.Should().BeFalse());
        MSTestAssertions.That(() => result.Errors.Should().NotBeEmpty());

        /*
        var validator = new UserValidator();

        validator.Validate(user); // default rules only
        validator.Validate(user, ["Create"]); // only Create rules
        validator.Validate(user, ["Update"]); // only Update rules
        validator.Validate(user, ["default", "Update"]); // default + Update
        */
    }

    [TestMethod]
    public void Should_Return_Error_When_Any_Property_Is_Missing() {
        var definition = new RegisterUserValidator();
        var validator = definition.Build();

        var result = validator.Validate(new RegisterUserRequest(Email: "", Password: "", CardNumber: ""));

        MSTestAssertions.That(() => result.IsValid.Should().BeFalse());
        MSTestAssertions.That(() => result.Errors.Should().NotBeEmpty());
    }

    [TestMethod]
    public void Should_Pass_Validation_When_All_Properties_Are_Valid() {
        var definition = new CreateClientValidator();
        var validator = definition.Build();
        var command = new CreateClientCommand {
            FirstName = "John",
            LastName = "Doe",
            Email = "john@example.com",
            Password = "Secret123",
            Age = 25,
            HomeAddress = new Address {
                Street = "Main St",
                City = "Geleen",
                PostalCode = "6164"
            }
        };

        var result = validator.Validate(command);

        if(!result.IsValid) {
            foreach(var error in result.Errors) {
                Console.WriteLine($"{error.MemberName}: {error.Message}");
            }
        }

        MSTestAssertions.That(() => result.IsValid.Should().BeTrue());
        MSTestAssertions.That(() => result.Errors.Should().BeEmpty());
    }
}