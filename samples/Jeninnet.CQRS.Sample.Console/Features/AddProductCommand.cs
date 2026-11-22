//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Sample.Console.Features;

// --- COMMAND: Add a Product ---
// Documentation: ICommand<int> tells the library this action returns an integer (the new ID).
public record AddProductCommand(string Name, decimal Price) : ICommand<int>;
