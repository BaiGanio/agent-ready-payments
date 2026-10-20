namespace Payments.Api.Tests;

/// <summary>
/// A clock that moves one fixed step every time it is asked. Whole seconds only,
/// so Postgres stores the value with no rounding and the round trip can be exact.
/// </summary>
public sealed class SteppingClock(DateTimeOffset start, TimeSpan step) : TimeProvider
{
    public static readonly DateTimeOffset Default = new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private DateTimeOffset _now = start;

    public SteppingClock() : this(Default, TimeSpan.FromMinutes(1)) { }

    public override DateTimeOffset GetUtcNow()
    {
        var now = _now;
        _now += step;
        return now;
    }
}