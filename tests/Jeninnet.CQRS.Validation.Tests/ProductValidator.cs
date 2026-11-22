//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Validation.Tests;

public class Product {
    public decimal? Price { get; internal set; }
    public int Stock { get; internal set; }
}

public sealed class ProductValidator
    : ValidatorDefinition<Product> {
    protected override void Configure(ValidatorRuleBuilder<Product> builder) {
        builder.RuleFor(x => x.Price, nameof(Product.Price))
               .GreaterThan(0m, "Price must be greater than zero.")
               .LessThanOrEqual(10_000m, "Price must not exceed 10,000.");

        builder.RuleFor(x => x.Stock, nameof(Product.Stock))
               .InclusiveBetween(0, 1_000, "Stock must be between 0 and 1000.");
    }
}
