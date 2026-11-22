//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sample.Console.Features;

public class AddProductHandler : ICommandHandler<AddProductCommand, int> {
    // A simple in-memory list acting as our database
    private static readonly List<Product> _products = new();

    public Task<int> HandleAsync(AddProductCommand command, CancellationToken cancellationToken) {
        var newId = _products.Count + 1;
        _products.Add(new Product(newId, command.Name, command.Price));

        System.Console.WriteLine($"[Handler] Success: Product '{command.Name}' added.");
        return Task.FromResult(newId);
    }
}
