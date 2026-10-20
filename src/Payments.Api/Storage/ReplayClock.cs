namespace Payments.Api.Storage;

/// <summary>
/// Hands back the timestamps the database already stored, in order.
/// The aggregate asks its clock for "now" once per operation, so a replay
/// gets the original times instead of today's.
/// </summary>
internal sealed class ReplayClock(IEnumerable<DateTimeOffset> stamps, TimeProvider afterReplay) : TimeProvider
{
    private readonly Queue<DateTimeOffset> _stamps = new(stamps);

    public override DateTimeOffset GetUtcNow()
    {
        return (_stamps.TryDequeue(out var next))
            ? next
            : afterReplay.GetUtcNow();
    }
}