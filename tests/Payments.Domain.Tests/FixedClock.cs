namespace Payments.Domain.Tests;

/// <summary>
/// A clock that never moves unless the test moves it.
/// The domain must take its timestamps from the injected <see cref="TimeProvider"/>,
/// never from <c>DateTimeOffset.UtcNow</c>. That is what makes these tests deterministic.
/// </summary>
public sealed class FixedClock(DateTimeOffset start) : TimeProvider
{
    public static readonly DateTimeOffset Default = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    private DateTimeOffset _now = start;

    public FixedClock() : this(Default) { }

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}
