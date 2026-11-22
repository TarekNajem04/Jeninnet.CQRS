//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Testing;

/// <summary>
/// Specification-style assertions over a <see cref="SagaTestResult{TState}"/>. Failures name the
/// expected and actual saga state or the follow-ups that were actually recorded.
/// </summary>
public static class SagaTestAssertions {
    /// <summary>Asserts that the saga is in the expected state after the event.</summary>
    /// <param name="result">The harness result.</param>
    /// <param name="state">The expected persisted state name.</param>
    /// <param name="because">An optional explanation.</param>
    public static void ShouldHaveTransitionedTo(this ISagaTestResult result, string state, string? because = null) {
        Guard.NotNull(result);
        Guard.NotNullOrWhiteSpace(state);

        result.StateName.Should()
              .Be(state, because ?? $"expected saga '{result.SagaId}' to be in state '{state}' but was '{result.StateName}'");
    }

    /// <summary>Asserts that a follow-up command or notification of the expected type was recorded.</summary>
    /// <typeparam name="TMessage">The follow-up message type.</typeparam>
    /// <param name="result">The harness result.</param>
    /// <param name="match">An optional predicate; <see langword="null"/> matches any instance of the type.</param>
    /// <param name="because">An optional explanation.</param>
    public static void ShouldHavePublished<TMessage>(
        this ISagaTestResult result,
        Func<TMessage, bool>? match = null,
        string? because = null) {
        Guard.NotNull(result);

        var actual = result.Published.OfType<TMessage>().ToList();
        actual.Should()
              .Contain(
                  message => match is null || match(message),
                  because ?? $"expected published {typeof(TMessage).Name} but found: {Describe(result.Published)}");
    }

    /// <summary>Asserts that no follow-up command or notification of the expected type was recorded.</summary>
    /// <typeparam name="TMessage">The follow-up message type.</typeparam>
    /// <param name="result">The harness result.</param>
    /// <param name="match">An optional predicate; <see langword="null"/> matches any instance of the type.</param>
    /// <param name="because">An optional explanation.</param>
    public static void ShouldNotHavePublished<TMessage>(
        this ISagaTestResult result,
        Func<TMessage, bool>? match = null,
        string? because = null) {
        Guard.NotNull(result);

        var actual = result.Published.OfType<TMessage>().ToList();
        actual.Should()
              .NotContain(
                  message => match is null || match(message),
                  because ?? $"expected no published {typeof(TMessage).Name} but found: {Describe(actual)}");
    }

    private static string Describe<T>(IReadOnlyCollection<T> actual) =>
        actual.Count is 0 ? "(nothing)" : string.Join(", ", actual.Select(static message => message?.GetType().Name ?? "(null)"));
}
