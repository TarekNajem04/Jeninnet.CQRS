//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Interfaces;

/// <summary>
/// Defines a contract for handling commands of type <typeparamref name="TCommand"/> and producing a response of type <typeparamref name="TResponse"/>.
/// </summary>
/// <remarks>
/// This interface extends <see cref="IHandler{TCommand, TResponse}"/> to provide specialized handling for commands.
/// Implementations of this interface are responsible for processing the command and returning the appropriate response.
/// </remarks>
/// <typeparam name="TCommand">The type of the command to be handled. Must implement <see cref="ICommand{TResponse}"/>.</typeparam>
/// <typeparam name="TResponse">The type of the response produced after handling the command.</typeparam>
public interface ICommandHandler<in TCommand, TResponse> : IHandler<TCommand, TResponse> where TCommand : ICommand<TResponse>;

/// <summary>
/// Defines a contract for handling commands of type <typeparamref name="TCommand"/> and producing.
/// </summary>
/// <remarks>
/// This interface extends <see cref="IHandler{TCommand, Unit}"/> to provide specialized handling for commands.
/// Implementations of this interface are responsible for processing the command and returning the appropriate response.
/// </remarks>
/// <typeparam name="TCommand">The type of the command to be handled. Must implement <see cref="ICommand"/>.</typeparam>
public interface ICommandHandler<in TCommand> : ICommandHandler<TCommand, Unit> where TCommand : ICommand;