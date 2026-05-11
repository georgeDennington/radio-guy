using RadioMan.Agents;
using RadioMan.Dcs;

namespace RadioMan.Conditions;

/// One proactive radio call the scheduler will deliver: which agent says it,
/// and what the message is.
public sealed record ScheduledCall(RadioAgent Agent, string Message);

/// A reactive check over current DCS state. Watches have a self-managed
/// lifecycle: they tick at their own Interval, may emit ScheduledCalls, and
/// auto-deregister when ShouldExit returns true or ExpiresAt is reached.
public sealed class Watch
{
    public required string Id { get; init; }
    public required TimeSpan Interval { get; init; }
    public required Func<IDcsClient, ScheduledCall?> OnTick { get; init; }
    public required Func<IDcsClient, bool> ShouldExit { get; init; }
    public DateTime ExpiresAt { get; init; } = DateTime.MaxValue;

    /// Internal: when the scheduler should next run this watch's OnTick.
    /// Mutated only by the scheduler.
    internal DateTime NextTickAt { get; set; } = DateTime.MinValue;
}
