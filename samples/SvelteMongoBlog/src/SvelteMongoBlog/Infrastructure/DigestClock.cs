namespace SvelteMongoBlog.Infrastructure;

public readonly record struct DigestWindow(
    DateOnly LocalDate,
    DateTimeOffset StartUtc,
    DateTimeOffset EndUtc);

/// <summary>Computes a user's local daily-digest window without assuming a fixed UTC offset.</summary>
public sealed class DigestClock(TimeProvider? timeProvider = null)
{
    public static readonly TimeOnly DefaultSendTime = new(8, 0);
    static readonly TimeSpan InvalidBoundarySearchLimit = TimeSpan.FromHours(48);

    readonly TimeProvider clock = timeProvider ?? TimeProvider.System;

    public DateTimeOffset UtcNow => clock.GetUtcNow();

    /// <summary>Returns yesterday's local-day window once the user's local time reaches 08:00.</summary>
    public bool TryGetDueWindow(string ianaTimeZoneId, out DigestWindow window) =>
        TryGetDueWindow(clock.GetUtcNow(), ianaTimeZoneId, out window);

    /// <summary>Static-time overload for deterministic tests and callers with a captured UTC instant.</summary>
    public static bool TryGetDueWindow(
        DateTimeOffset nowUtc,
        string ianaTimeZoneId,
        out DigestWindow window)
    {
        var zone = FindIanaTimeZone(ianaTimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(nowUtc.ToUniversalTime(), zone);
        if (TimeOnly.FromDateTime(localNow.DateTime) < DefaultSendTime)
        {
            window = default;
            return false;
        }

        var today = DateOnly.FromDateTime(localNow.DateTime);
        var yesterday = today.AddDays(-1);
        window = new DigestWindow(
            yesterday,
            GetLocalDayBoundaryUtc(yesterday, zone),
            GetLocalDayBoundaryUtc(today, zone));
        return true;
    }

    /// <summary>Accepts IANA IDs (including UTC) and rejects Windows time-zone display names.</summary>
    public static bool IsValidIanaTimeZone(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)
            || !string.Equals(timeZoneId, timeZoneId.Trim(), StringComparison.Ordinal)
            || timeZoneId.Any(char.IsWhiteSpace)
            || timeZoneId.Contains('\\'))
            return false;

        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return string.Equals(timeZoneId, "UTC", StringComparison.OrdinalIgnoreCase) || zone.HasIanaId;
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static TimeZoneInfo FindIanaTimeZone(string timeZoneId)
    {
        if (!IsValidIanaTimeZone(timeZoneId))
            throw new ArgumentException("A valid IANA time-zone ID is required.", nameof(timeZoneId));
        return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    }

    /// <summary>Returns the next local 08:00 instant, converted with the zone's current rules.</summary>
    public static DateTimeOffset NextDueUtc(string ianaTimeZoneId, DateTimeOffset nowUtc)
    {
        var zone = FindIanaTimeZone(ianaTimeZoneId);
        var now = nowUtc.ToUniversalTime();
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        var dueDate = DateOnly.FromDateTime(localNow.DateTime);
        var dueLocal = DateTime.SpecifyKind(dueDate.ToDateTime(DefaultSendTime), DateTimeKind.Unspecified);
        var localNowUnspecified = DateTime.SpecifyKind(localNow.DateTime, DateTimeKind.Unspecified);
        if (dueLocal <= localNowUnspecified)
        {
            dueDate = dueDate.AddDays(1);
            dueLocal = DateTime.SpecifyKind(dueDate.ToDateTime(DefaultSendTime), DateTimeKind.Unspecified);
        }

        // 08:00 is not normally a transition time, but resolve gaps and overlaps deterministically.
        if (zone.IsInvalidTime(dueLocal)) dueLocal = FirstValidLocalTime(dueLocal, zone);
        if (zone.IsAmbiguousTime(dueLocal))
            return new DateTimeOffset(dueLocal, zone.GetAmbiguousTimeOffsets(dueLocal).Max()).ToUniversalTime();
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(dueLocal, zone), TimeSpan.Zero);
    }

    /// <summary>
    /// Converts local midnight to UTC. If midnight is skipped, uses the first valid minute after it;
    /// if midnight repeats, chooses the earlier UTC occurrence. The same rule is used for both ends
    /// of adjacent days, so their windows remain contiguous through DST changes.
    /// </summary>
    public static DateTimeOffset GetLocalDayBoundaryUtc(DateOnly localDate, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var local = DateTime.SpecifyKind(localDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        var searchEnd = local + InvalidBoundarySearchLimit;
        if (timeZone.IsInvalidTime(local))
        {
            var lastInvalid = local;
            while (timeZone.IsInvalidTime(local) && local < searchEnd)
            {
                lastInvalid = local;
                local = local.AddMinutes(1);
            }

            // Minute stepping brackets the transition; refine to its first valid tick so historical
            // zones with a non-minute transition do not lose part of the local calendar day.
            if (!timeZone.IsInvalidTime(local))
            {
                var low = lastInvalid.Ticks;
                var high = local.Ticks;
                while (high - low > 1)
                {
                    var middle = low + ((high - low) / 2);
                    var candidate = new DateTime(middle, DateTimeKind.Unspecified);
                    if (timeZone.IsInvalidTime(candidate)) low = middle;
                    else high = middle;
                }
                local = new DateTime(high, DateTimeKind.Unspecified);
            }
        }

        if (timeZone.IsInvalidTime(local))
            throw new InvalidTimeZoneException("The local day boundary remains invalid for more than 48 hours.");

        if (timeZone.IsAmbiguousTime(local))
        {
            // UTC = local - offset; the greatest offset therefore yields the earliest UTC instant.
            var earlierOffset = timeZone.GetAmbiguousTimeOffsets(local).Max();
            return new DateTimeOffset(local, earlierOffset).ToUniversalTime();
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone), TimeSpan.Zero);
    }

    static DateTime FirstValidLocalTime(DateTime local, TimeZoneInfo zone)
    {
        var searchEnd = local + InvalidBoundarySearchLimit;
        var lastInvalid = local;
        while (zone.IsInvalidTime(local) && local < searchEnd)
        {
            lastInvalid = local;
            local = local.AddMinutes(1);
        }
        if (zone.IsInvalidTime(local))
            throw new InvalidTimeZoneException("The local digest time remains invalid for more than 48 hours.");
        var low = lastInvalid.Ticks;
        var high = local.Ticks;
        while (high - low > 1)
        {
            var middle = low + ((high - low) / 2);
            var candidate = new DateTime(middle, DateTimeKind.Unspecified);
            if (zone.IsInvalidTime(candidate)) low = middle;
            else high = middle;
        }
        return new DateTime(high, DateTimeKind.Unspecified);
    }
}

