//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Tests;

/// <summary>
/// Cron table tests for CQRS-38: wildcards, steps, ranges, lists, weekday normalization, and the
/// combined DOM/DOW case <c>*/15 0-6 1,15 * 1-5</c>.
/// </summary>
[TestClass]
public class CronExpressionTests {
    private static readonly DateTimeOffset _newYear = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void Steps_Ranges_And_Lists_AreHonored() {
        // Arrange
        var everyFifteen = CronExpression.Parse("*/15 * * * *");
        var listAndRange = CronExpression.Parse("5,10,15-20/2 * * * *");

        // Act / Assert
        MSTestAssertions.That(() => everyFifteen.GetNextOccurrence(_newYear.AddMinutes(7)).Should().Be(_newYear.AddMinutes(15)));
        MSTestAssertions.That(() => listAndRange.GetNextOccurrence(_newYear.AddMinutes(10)).Should().Be(_newYear.AddMinutes(15)));
        MSTestAssertions.That(() => listAndRange.GetNextOccurrence(_newYear.AddMinutes(16)).Should().Be(_newYear.AddMinutes(17)));
        MSTestAssertions.That(() => listAndRange.GetNextOccurrence(_newYear.AddMinutes(18)).Should().Be(_newYear.AddMinutes(19)));
    }

    [TestMethod]
    public void Weekdays_RollOverTheWeekend() {
        // Arrange: Friday 20 Nov 2026, 18:00.
        var fridayEvening = new DateTimeOffset(2026, 11, 20, 18, 0, 0, TimeSpan.Zero);
        var expression = CronExpression.Parse("0 9-17 * * 1-5");

        // Act
        var next = expression.GetNextOccurrence(fridayEvening);

        // Assert: Monday 23 Nov 2026, 09:00.
        MSTestAssertions.That(() => next.Should().Be(new DateTimeOffset(2026, 11, 23, 9, 0, 0, TimeSpan.Zero)));
    }

    [TestMethod]
    public void CombinedDayOfMonthAndWeekday_UsesStandardOrSemantics() {
        // Arrange: 1 Nov 2026 is a Sunday and matches the DOM field (1).
        var expression = CronExpression.Parse("*/15 0-6 1,15 * 1-5");

        // Act
        var fromSaturday = expression.GetNextOccurrence(new DateTimeOffset(2026, 10, 31, 23, 59, 0, TimeSpan.Zero));
        var fromSundayMidnight = expression.GetNextOccurrence(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));

        // Assert: DOM and DOW are OR-ed when both are restricted.
        MSTestAssertions.That(() => fromSaturday.Should().Be(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)));
        MSTestAssertions.That(() => fromSundayMidnight.Should().Be(new DateTimeOffset(2026, 11, 1, 0, 15, 0, TimeSpan.Zero)));
    }

    [TestMethod]
    public void SundaySeven_IsNormalizedToSundayZero() {
        // Arrange: Saturday 21 Nov 2026.
        var expression = CronExpression.Parse("0 0 * * 7");

        // Act
        var next = expression.GetNextOccurrence(new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero));

        // Assert
        MSTestAssertions.That(() => next.Should().Be(new DateTimeOffset(2026, 11, 22, 0, 0, 0, TimeSpan.Zero)));
    }

    [TestMethod]
    public void TimeZone_IsAppliedToTheCronFields() {
        // Arrange: 09:00 UTC is 10:00 in a fixed UTC+1 zone.
        var expression = CronExpression.Parse("0 10 * * *");
        var berlin = TimeZoneInfo.CreateCustomTimeZone("CET", TimeSpan.FromHours(1), "CET", "CET");

        // Act
        var next = expression.GetNextOccurrence(_newYear, berlin);

        // Assert
        MSTestAssertions.That(() => next.Should().Be(_newYear.AddHours(9)));
    }

    [TestMethod]
    public void InvalidExpressions_AreRejected() {
        // Act / Assert
        foreach(var expression in new[] { "* * * *", "*/0 * * * *", "60 * * * *", "0 25 * * *", "0 0 32 * *", "0 0 * 13 *", "0 0 * * 8", "a * * * *" }) {
            var thrown = Capture(() => CronExpression.Parse(expression));
            MSTestAssertions.That(() => thrown.Should().BeAssignableTo<ArgumentException>());
        }
    }

    private static Exception? Capture(Action action) {
        try {
            action();

            return null;
        }
        catch(Exception exception) {
            return exception;
        }
    }
}