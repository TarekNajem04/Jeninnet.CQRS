//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sample.Console.Features;

// --- QUERY: Get Product Details ---
// Documentation: IQuery<Product> tells the library this fetches a Product object.
public record GetProductQuery(int Id) : IQuery<Product>;