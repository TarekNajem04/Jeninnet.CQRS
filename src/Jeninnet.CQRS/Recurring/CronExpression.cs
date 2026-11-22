//
// Copyright (c) Jeninnet.
// Part of the Jeninnet Platform.
// Solution Repository: https://github.com/TarekNajem04/Jeninnet.CQRS
// Licensed under the MIT License.
//
namespace Jeninnet.CQRS.Recurring;

/// <summary>
/// A five-field cron expression: minute, hour, day-of-month, month, day-of-week. Supports
/// <c>*</c>, steps (<c>*/n</c>, <c>a-b/n</c>), ranges, and comma lists. Day-of-week accepts
/// 0-7 where both 0 and 7 mean Sunday. Standard cron day semantics apply: when both day fields
/// are restricted, either may match; otherwise both must.
/// </summary>
public sealed class CronExpression {
    private const int MAX_SEARCH_YEARS = 5;
    private readonly ulong _minutes;
    private readonly ulong _hours;
    private readonly ulong _days;
    private readonly ulong _months;
    private readonly ulong _weekdays;
    private readonly bool _dayRestricted;
    private readonly bool _weekdayRestricted;

    private CronExpression(string expression, ulong minutes, ulong hours, ulong days, ulong months, ulong weekdays, bool dayRestricted, bool weekdayRestricted) { // NOSONAR: Public options/records intentionally group related settings; splitting would fragment the API.
        Expression = expression;
        _minutes = minutes;
        _hours = hours;
        _days = days;
        _months = months;
        _weekdays = weekdays;
        _dayRestricted = dayRestricted;
        _weekdayRestricted = weekdayRestricted;
    }

    /// <summary>Gets the original expression text.</summary>
    public string Expression { get; }

    /// <summary>
    /// Parses a five-field cron expression.
    /// </summary>
    /// <param name="expression">The expression text.</param>
    /// <exception cref="ArgumentException">
    /// The expression is empty, does not have five fields, or contains an invalid/out-of-range part.
    /// </exception>
    public static CronExpression Parse(string expression) {
        Guard.NotNullOrWhiteSpace(expression);
        var fields = expression
            .Split([' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(static field => field.Trim())
            .ToArray();

        if(fields.Length is not 5) {
            throw new ArgumentException($"A cron expression needs exactly five fields, got {fields.Length}.", nameof(expression));
        }

        var minutes = ParseField(fields[0], 0, 59, nameof(expression));
        var hours = ParseField(fields[1], 0, 23, nameof(expression));
        var days = ParseField(fields[2], 1, 31, nameof(expression));
        var months = ParseField(fields[3], 1, 12, nameof(expression));
        var weekdays = ParseField(fields[4], 0, 7, nameof(expression), dayOfWeek: true);

        // Normalize Sunday 7 to 0 when the range/list included it.
        if((weekdays & (1UL << 7)) != 0) {
            weekdays = (weekdays & ~(1UL << 7)) | 1UL;
        }

        return new CronExpression(
            expression,
            minutes,
            hours,
            days,
            months,
            weekdays,
            dayRestricted: !IsWildcard(fields[2]),
            weekdayRestricted: !IsWildcard(fields[4]));
    }

    /// <summary>Gets whether the local time matches the expression.</summary>
    /// <param name="localTime">A wall-clock time in the schedule's time zone.</param>
    public bool Matches(DateTimeOffset localTime) {
        if(!Has(_minutes, localTime.Minute) || !Has(_hours, localTime.Hour) || !Has(_months, localTime.Month)) {
            return false;
        }

        var dayMatches = Has(_days, localTime.Day);
        var weekdayMatches = Has(_weekdays, (int)localTime.DayOfWeek);

        return _dayRestricted && _weekdayRestricted
            ? dayMatches || weekdayMatches
            : dayMatches && weekdayMatches;
    }

    /// <summary>
    /// Gets the first occurrence strictly after <paramref name="afterUtc"/> in
    /// <paramref name="timeZone"/> (UTC when omitted). Searches minute by minute and skips
    /// DST-invalid wall-clock times naturally because the time is converted from UTC.
    /// </summary>
    /// <param name="afterUtc">The exclusive lower bound.</param>
    /// <param name="timeZone">The schedule time zone; null means UTC.</param>
    /// <exception cref="InvalidOperationException">No occurrence exists within five years.</exception>
    public DateTimeOffset GetNextOccurrence(DateTimeOffset afterUtc, TimeZoneInfo? timeZone = null) {
        var zone = timeZone ?? TimeZoneInfo.Utc;
        var utc = afterUtc.ToUniversalTime();
        var candidate = new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, utc.Minute, 0, TimeSpan.Zero).AddMinutes(1);
        var limit = MAX_SEARCH_YEARS * 366 * 24 * 60;

        for(var index = 0; index < limit; index++) {
            var local = TimeZoneInfo.ConvertTime(candidate, zone);

            if(Matches(local)) {
                return candidate;
            }

            candidate = candidate.AddMinutes(1);
        }

        throw new InvalidOperationException($"Cron expression '{Expression}' has no occurrence within {MAX_SEARCH_YEARS} years.");
    }

    private static bool IsWildcard(string field) =>
        field is "*" || field.StartsWith("*/", StringComparison.Ordinal);

    private static bool Has(ulong mask, int value) => (mask & (1UL << value)) != 0;

    private static ulong ParseField(string field, int minimum, int maximum, string parameterName, bool dayOfWeek = false) {
        if(StringCompat.IsNullOrWhiteSpace(field)) {
            throw new ArgumentException("A cron field cannot be empty.", parameterName);
        }

        var mask = 0UL;

        foreach(var part in field.Split(',')) {
            mask |= ParsePart(part, minimum, maximum, parameterName, dayOfWeek);
        }

        return mask;
    }

    private static ulong ParsePart(string part, int minimum, int maximum, string parameterName, bool dayOfWeek) {
        var step = 1;
        var slash = part.IndexOf('/');

        if(slash >= 0) {
            if(!Int32Compat.TryParse(part.AsSpan(slash + 1), out step) || step < 1) {
                throw new ArgumentException($"Invalid cron step '{part}'.", parameterName);
            }

            part = part.Substring(0, slash);
        }

        int start;
        int end;

        if(part is "*") {
            start = minimum;
            end = maximum;
        } else {
            var dash = part.IndexOf('-');

            if(dash >= 0) {
                start = ParseValue(part.Substring(0, dash), minimum, maximum, dayOfWeek, parameterName);
                end = ParseValue(part.Substring(dash + 1), minimum, maximum, dayOfWeek, parameterName);

                if(start > end) {
                    throw new ArgumentException($"Invalid cron range '{part}'.", parameterName);
                }
            } else {
                start = ParseValue(part, minimum, maximum, dayOfWeek, parameterName);
                end = start;
            }
        }

        var mask = 0UL;

        for(var value = start; value <= end; value += step) {
            if(value > maximum) {
                break;
            }

            mask |= 1UL << value;
        }

        return mask;
    }

    private static int ParseValue(string value, int minimum, int maximum, bool dayOfWeek, string parameterName) {
        if(!int.TryParse(value, out var parsed)) {
            throw new ArgumentException($"Invalid cron value '{value}'.", parameterName);
        }

        if(parsed < minimum || parsed > maximum) {
            throw new ArgumentException($"Cron value {parsed} is outside {minimum}-{maximum}.", parameterName);
        }

        // Sunday is 0 or 7; the range loop normalizes bit 7 before use.
        _ = dayOfWeek;

        return parsed;
    }
}
