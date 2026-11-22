//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Generators.Models;

/// <summary>
/// Internal representation of a discovered CQRS handler.
/// </summary>
public record HandlerDiscoveryModel(
    string HandlerType,
    string InterfaceType,
    string AssemblyName,
    string SourceLocation
);
