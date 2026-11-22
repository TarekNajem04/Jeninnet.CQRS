//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sample.Console.Features;

public class GetProductHandler : IQueryHandler<GetProductQuery, Product?> {
    public Task<Product?> HandleAsync(GetProductQuery query, CancellationToken cancellationToken) =>
        // In a real app, this would be an EF Core call: await _context.Products.FindAsync(query.Id);
        Task.FromResult<Product?>(new Product(query.Id, "Sample Product", 99.99m));
}
