namespace Macrofy.Core.Tests;

internal sealed class PlaybackTestClock : TimeProvider
{
    private readonly object sync = new();
    private readonly List<TestTimer> timers = [];
    private long timestamp;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() { lock (sync) return timestamp; }
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());
    public int PendingTimers { get { lock (sync) return timers.Count(t => !t.Disposed && t.Due != long.MaxValue); } }
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        lock (sync)
        {
            var timer = new TestTimer(this, callback, state);
            timers.Add(timer);
            timer.Change(dueTime, period);
            return timer;
        }
    }
    public void Advance(int milliseconds)
    {
        List<TestTimer> due;
        lock (sync)
        {
            timestamp += TimeSpan.FromMilliseconds(milliseconds).Ticks;
            due = timers.Where(t => !t.Disposed && t.Due <= timestamp).ToList();
            foreach (var timer in due) timer.Due = timer.Period > 0 ? timestamp + timer.Period : long.MaxValue;
        }
        foreach (var timer in due) timer.Callback(timer.State);
    }
    private sealed class TestTimer(PlaybackTestClock owner, TimerCallback callback, object? state) : ITimer
    {
        public TimerCallback Callback { get; } = callback;
        public object? State { get; } = state;
        public long Due { get; set; }
        public long Period { get; private set; }
        public bool Disposed { get; private set; }
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (owner.sync)
            {
                if (Disposed) return false;
                Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : owner.timestamp + dueTime.Ticks;
                Period = period == Timeout.InfiniteTimeSpan ? -1 : period.Ticks;
                return true;
            }
        }
        public void Dispose() { lock (owner.sync) { Disposed = true; owner.timers.Remove(this); } }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
