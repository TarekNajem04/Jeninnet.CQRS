//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Validation.Tests;

#region Domain Model
public sealed class Order {
    public List<OrderItem> Items { get; init; } = [];
}

public sealed class OrderItem {
    public Guid? Id { get; init; }
}
#endregion

public sealed class OrderValidator : ValidatorDefinition<Order> {
    protected override void Configure(ValidatorRuleBuilder<Order> builder) =>
        builder.RuleFor(x => x.Items, nameof(Order.Items))
               .NotEmpty("Order must contain at least one item.")
               .ForEach(new OrderItemValidator().Build())
               ;
}

public sealed class OrderItemValidator : ValidatorDefinition<OrderItem> {
    protected override void Configure(ValidatorRuleBuilder<OrderItem> builder) =>
        builder.RuleFor(x => x.Id, nameof(OrderItem.Id))
               .NotEmpty("Order must contain at least one item.");
}
