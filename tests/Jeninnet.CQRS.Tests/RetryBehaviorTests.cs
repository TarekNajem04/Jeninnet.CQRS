//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

[TestClass]
public class RetryBehaviorTests {
    [TestMethod]
    public async Task HandleAsync_ShouldRetryAndSucceed_OnTransientFailureAsync() {
        // Arrange
        var options = Options.Create(new RetryBehaviorOptions {
            RetryCount = 3,
            BaseDelayMilliseconds = 10 // Short delay for fast tests
        });

        var behavior = new RetryBehavior<TestCommand, string>(options);
        var attempts = 0;

        Task<string> next(TestCommand _, CancellationToken __) {
            attempts++;
            if(attempts < 3) {
                throw new InvalidOperationException("Temporary failure");
            }

            return Task.FromResult("SuccessAfterRetry");
        }

        // Act
        var result = await behavior.HandleAsync(new TestCommand("Data"), next, default);

        // Assert
        MSTestAssertions.That(() => result.Should().Be("SuccessAfterRetry"));
        MSTestAssertions.That(() => attempts.Should().Be(3, "The logic should have retried twice and succeeded on the third attempt."));
    }

    private sealed record TestCommand(string Message) : ICommand<string>;
}