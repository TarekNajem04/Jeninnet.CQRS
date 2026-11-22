//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Validation.Tests;

public class PriceRange {
    public decimal? Min { get; internal set; }
    public int Max { get; internal set; }
}

public sealed class PriceRangeValidator
    : ValidatorDefinition<PriceRange> {
    protected override void Configure(ValidatorRuleBuilder<PriceRange> builder) {
        builder.RuleFor(x => x.Min, nameof(PriceRange.Min))
               .GreaterThanOrEqual(0m, "Min must be >= 0.");

        builder.RuleFor(x => x.Max, nameof(PriceRange.Max))
               .LessThanOrEqual(1000, "Max must be <= 1000.");
    }
}
