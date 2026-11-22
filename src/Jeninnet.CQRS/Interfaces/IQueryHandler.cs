//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Interfaces;

/// <summary>
/// Defines a contract for handling queries of a specified type and returning a result.
/// </summary>
/// <remarks>
/// This interface is typically used in a CQRS (Command Query Responsibility Segregation) pattern todecouple query handling logic from the rest of the application.
/// Implementations of this interface should provide the logic to process the query and return the corresponding result.
/// </remarks>
/// <typeparam name="TQuery">The type of the query to be handled. Must implement <see cref="IQuery{TResult}"/>.</typeparam>
/// <typeparam name="TResult">The type of the result produced by handling the query.</typeparam>
public interface IQueryHandler<in TQuery, TResult> : IHandler<TQuery, TResult> where TQuery : IQuery<TResult>;
