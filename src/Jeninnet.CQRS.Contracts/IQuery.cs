//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Interfaces;

/// <summary>
/// Represents a query that, when executed, produces a result of the specified type.
/// </summary>
/// <typeparam name="TResult">The type of the result produced by the query.</typeparam>
public interface IQuery<out TResult>;