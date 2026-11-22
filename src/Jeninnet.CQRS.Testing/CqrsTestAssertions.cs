//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Testing;

/// <summary>
/// Specification-style assertions over <see cref="TestCqrsBroker"/> recordings and
/// <see cref="BehaviorCallLog"/> entries. Failures read like specifications because they list
/// what was actually dispatched.
/// </summary>
public static class CqrsTestAssertions {
    /// <summary>
    /// Asserts that a command matching <paramref name="match"/> (or any command of the type
    /// when <see langword="null"/>) was dispatched.
    /// </summary>
    public static void ShouldHaveDispatched<TCommand>(this TestCqrsBroker broker, Func<TCommand, bool>? match = null, string? because = null) {
        Guard.NotNull(broker);

        var actual = broker.DispatchedCommands.OfType<TCommand>().ToList();

        actual.Should()
              .Contain(m => match is null || match(m),
                    because ?? $"expected dispatched {typeof(TCommand).Name} but found: {Describe(broker.DispatchedCommands)}");
    }

    /// <summary>
    /// Asserts that no command matching <paramref name="match"/> (or of the type at all when
    /// <see langword="null"/>) was dispatched.
    /// </summary>
    public static void ShouldNotHaveDispatched<TCommand>(this TestCqrsBroker broker, Func<TCommand, bool>? match = null, string? because = null) {
        Guard.NotNull(broker);

        var actual = broker.DispatchedCommands.OfType<TCommand>().ToList();

        actual.Should()
              .NotContain(m => match is null || match(m),
                    because ?? $"expected no dispatched {typeof(TCommand).Name} but found: {Describe(actual)}");
    }

    /// <summary>
    /// Asserts that a notification matching <paramref name="match"/> (or any notification of
    /// the type when <see langword="null"/>) was published.
    /// </summary>
    public static void ShouldHavePublished<TNotification>(this TestCqrsBroker broker, Func<TNotification, bool>? match = null, string? because = null) {
        Guard.NotNull(broker);

        var actual = broker.PublishedNotifications.OfType<TNotification>().ToList();

        actual.Should()
              .Contain(m => match is null || match(m),
                    because ?? $"expected published {typeof(TNotification).Name} but found: {Describe(broker.PublishedNotifications)}");
    }

    /// <summary>
    /// Asserts that no notification matching <paramref name="match"/> (or of the type at all
    /// when <see langword="null"/>) was published.
    /// </summary>
    public static void ShouldNotHavePublished<TNotification>(this TestCqrsBroker broker, Func<TNotification, bool>? match = null, string? because = null) {
        Guard.NotNull(broker);

        var actual = broker.PublishedNotifications.OfType<TNotification>().ToList();

        actual.Should()
              .NotContain(m => match is null || match(m),
                    because ?? $"expected no published {typeof(TNotification).Name} but found: {Describe(actual)}");
    }

    /// <summary>
    /// Asserts that a behavior of type <typeparamref name="TBehavior"/> ran at least once.
    /// </summary>
    public static void ShouldHaveRunBehavior<TBehavior>(this BehaviorCallLog log, string? because = null) {
        Guard.NotNull(log);

        var name = typeof(TBehavior).FullName ?? typeof(TBehavior).Name;

        log.Calls.Should()
           .Contain(name,
                because ?? $"expected behavior {typeof(TBehavior).Name} to have run but ran: {Describe(log.Calls)}");
    }

    private static string Describe<T>(IReadOnlyCollection<T> actual) =>
        actual.Count is 0 ? "(nothing)" : string.Join(", ", actual.Select(static m => m?.ToString() ?? "(null)"));
}