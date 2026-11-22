//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Interfaces;

/// <summary>
/// Represents an operation or action that can be executed and produces a result of the specified type.
/// </summary>
/// <typeparam name="TResult">The type of the result produced by the command when executed.</typeparam>
public interface ICommand<out TResult>;

/// <summary>
/// Defines a command that can be executed, with no return value and no input parameters.
/// </summary>
/// <remarks>
/// This interface represents a command pattern implementation where the command encapsulates an action to be executed.
/// It is commonly used in scenarios such as UI interactions, command dispatching, or other cases where actions need to be decoupled from their execution logic.
/// </remarks>
public interface ICommand : ICommand<Jeninnet.CQRS.Unit>;
