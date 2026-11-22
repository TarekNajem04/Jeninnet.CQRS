//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//

namespace Jeninnet.CQRS.Conventions;

/// <summary>
/// Discovers convention-based handlers and registers adapters for them. Explicit interface
/// registrations always win: a type that implements a handler contract is never re-bound.
/// </summary>
internal static class ConventionHandlerRegistration {
    internal static void Register(IServiceCollection services, IEnumerable<Type> types) { // NOSONAR: Complexity is inherent to this state machine/pipeline and is covered by tests.
        var explicitHandlers = new HashSet<Type>();

        foreach(var type in types) {
            foreach(var contract in type.GetInterfaces()) { // NOSONAR: Explicit loop is clearer and faster than a LINQ pipeline here.
                if(IsHandlerContract(contract)) {
                    explicitHandlers.Add(contract.GetGenericArguments()[0]);
                }
            }
        }

        var claimed = new HashSet<Type>(explicitHandlers);

        foreach(var type in types.OrderBy(static t => t.FullName, StringComparer.Ordinal)) {
            if(!type.IsClass || type.IsAbstract || type.ContainsGenericParameters || ImplementsHandlerContract(type)) {
                continue;
            }

            foreach(var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance)) {
                if(!IsConventionMethod(method, out var messageType, out var resultType)) {
                    continue;
                }

                if(!claimed.Add(messageType)) {
                    // An explicit handler owns this message, or an earlier convention handler
                    // claimed it first: deterministic first-wins (types are name-ordered).
                    continue;
                }

                RegisterAdapter(services, type, messageType, resultType);
            }
        }
    }

    private static void RegisterAdapter(IServiceCollection services, Type handlerType, Type messageType, Type? resultType) {
        Type adapterType;
        Type serviceType;

        if(IsNotification(messageType)) {
            adapterType = typeof(ConventionNotificationHandler<,>).MakeGenericType(handlerType, messageType);
            serviceType = typeof(INotificationHandler<>).MakeGenericType(messageType);
        } else if(resultType is null) {
            adapterType = typeof(ConventionCommandHandler<,>).MakeGenericType(handlerType, messageType);
            serviceType = typeof(ICommandHandler<>).MakeGenericType(messageType);
        } else if(IsQuery(messageType)) {
            adapterType = typeof(ConventionQueryHandler<,,>).MakeGenericType(handlerType, messageType, resultType);
            serviceType = typeof(IQueryHandler<,>).MakeGenericType(messageType, resultType);
        } else {
            adapterType = typeof(ConventionCommandHandler<,,>).MakeGenericType(handlerType, messageType, resultType);
            serviceType = typeof(ICommandHandler<,>).MakeGenericType(messageType, resultType);
        }

        services.TryAddTransient(handlerType);
        services.AddTransient(serviceType, provider => Activator.CreateInstance(adapterType, provider.GetRequiredService(handlerType))!);
    }

    private static bool IsConventionMethod(MethodInfo method, out Type messageType, out Type? resultType) {
        messageType = typeof(object);
        resultType = null;

        if(!string.Equals(method.Name, ConventionHandlerInvoker.HandleAsyncName, StringComparison.Ordinal) &&
           !string.Equals(method.Name, ConventionHandlerInvoker.HandleName, StringComparison.Ordinal)) {
            return false;
        }

        if(method.IsStatic || method.IsGenericMethodDefinition || !typeof(Task).IsAssignableFrom(method.ReturnType)) {
            return false;
        }

        var parameters = method.GetParameters();

        if(parameters.Length is not (1 or 2)) {
            return false;
        }

        if(parameters.Length is 2 && parameters[1].ParameterType != typeof(CancellationToken)) {
            return false;
        }

        messageType = parameters[0].ParameterType;

        if(IsNotification(messageType)) {
            return method.ReturnType == typeof(Task);
        }

        if(!IsCommand(messageType) && !IsQuery(messageType)) {
            return false;
        }

        var returnsValue = method.ReturnType.IsGenericType && method.ReturnType.GetGenericTypeDefinition() == typeof(Task<>);

        if(returnsValue) {
            resultType = method.ReturnType.GetGenericArguments()[0];
        }

        return true;
    }

    private static bool IsNotification(Type type) =>
        typeof(INotification).IsAssignableFrom(type);

    private static bool IsCommand(Type type) =>
        typeof(ICommand).IsAssignableFrom(type)
        || type.GetInterfaces().Any(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICommand<>));

    private static bool IsQuery(Type type) =>
        type.GetInterfaces().Any(static i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IQuery<>));

    private static bool ImplementsHandlerContract(Type type) =>
        type.GetInterfaces().Any(IsHandlerContract);

    private static bool IsHandlerContract(Type contract) =>
        contract.IsGenericType && contract.GetGenericArguments().Length is 1 or 2 &&
        Unadorned(contract) is "ICommandHandler" or "IQueryHandler" or "IStreamQueryHandler" or "INotificationHandler";

    // Reflection appends the arity to generic type names ("ICommandHandler`2").
    private static string Unadorned(Type type) {
        var name = type.Name;
        var tick = name.IndexOf('`');

        return tick < 0 ? name : name.Substring(0, tick);
    }
}