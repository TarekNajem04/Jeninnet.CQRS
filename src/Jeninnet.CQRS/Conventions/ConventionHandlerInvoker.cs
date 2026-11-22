//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

namespace Jeninnet.CQRS.Conventions;

/// <summary>
/// Binds a convention handler method (<c>Handle</c>/<c>HandleAsync</c>) to a strongly typed
/// delegate. Compilation happens once per closed adapter type; the delegate itself is
/// allocation-free per call.
/// </summary>
/// <remarks>
/// The reflection registration path uses this binder; source-generated registration emits direct
/// calls instead, so trimmed/AOT applications never reach this code.
/// </remarks>
internal static class ConventionHandlerInvoker {
    internal const string HandleName = "Handle";
    internal const string HandleAsyncName = "HandleAsync";

    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Compiles an expression tree for convention handlers; use generated registration under NativeAOT.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Reflects over convention handler methods; use generated registration when trimming.")]
    public static Func<THandler, TMessage, CancellationToken, Task<TResult>> BindResult<THandler, TMessage, TResult>()
        where THandler : class {
        var method = FindMethod<THandler>(typeof(TMessage), typeof(Task<TResult>))
            ?? throw new InvalidOperationException($"No convention method returning Task<{typeof(TResult).Name}> accepting {typeof(TMessage).Name} on {typeof(THandler).Name}.");

        var handler = Expression.Parameter(typeof(THandler), "handler");
        var message = Expression.Parameter(typeof(TMessage), "message");
        var token = Expression.Parameter(typeof(CancellationToken), "cancellationToken");
        var call = method.GetParameters().Length is 2
            ? Expression.Call(handler, method, message, token)
            : Expression.Call(handler, method, message);

        return Expression.Lambda<Func<THandler, TMessage, CancellationToken, Task<TResult>>>(call, handler, message, token)
                         .Compile();
    }

    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Compiles an expression tree for convention handlers; use generated registration under NativeAOT.")]
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Reflects over convention handler methods; use generated registration when trimming.")]
    public static Func<THandler, TMessage, CancellationToken, Task> BindVoid<THandler, TMessage>()
        where THandler : class {
        var method = FindMethod<THandler>(typeof(TMessage), typeof(Task))
            ?? throw new InvalidOperationException($"No convention method returning Task accepting {typeof(TMessage).Name} on {typeof(THandler).Name}.");

        var handler = Expression.Parameter(typeof(THandler), "handler");
        var message = Expression.Parameter(typeof(TMessage), "message");
        var token = Expression.Parameter(typeof(CancellationToken), "cancellationToken");
        var call = method.GetParameters().Length is 2
            ? Expression.Call(handler, method, message, token)
            : Expression.Call(handler, method, message);

        return Expression.Lambda<Func<THandler, TMessage, CancellationToken, Task>>(call, handler, message, token)
                         .Compile();
    }

    /// <summary>Wraps a <see cref="Task"/> as a <see cref="Task{TResult}"/> of <see cref="Unit"/>.</summary>
    internal static async Task<Unit> AsUnitAsync(Task task) {
        await task.ConfigureAwait(false);

        return Unit.Value;
    }

    private static System.Reflection.MethodInfo? FindMethod<THandler>(Type messageType, Type returnType) {
        foreach(var method in typeof(THandler).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)) {
            if(!string.Equals(method.Name, HandleName, StringComparison.Ordinal) &&
               !string.Equals(method.Name, HandleAsyncName, StringComparison.Ordinal)) {
                continue;
            }

            if(method.IsGenericMethodDefinition || method.IsStatic || method.ReturnType != returnType) {
                continue;
            }

            var parameters = method.GetParameters();

            if(parameters.Length is not (1 or 2) || parameters[0].ParameterType != messageType) {
                continue;
            }

            if(parameters.Length is 2 && parameters[1].ParameterType != typeof(CancellationToken)) {
                continue;
            }

            return method;
        }

        return null;
    }
}