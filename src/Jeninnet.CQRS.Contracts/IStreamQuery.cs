//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Interfaces;

/// <summary>
/// Marker for a query whose handler streams multiple items instead of returning one result.
/// </summary>
/// <typeparam name="TItem">The streamed item type.</typeparam>
public interface IStreamQuery<out TItem>;
