namespace HomeStack.Core;

public static class TimeProviderExtensions
{
    /// <summary>Today's date in the server's local time zone (set TZ in Docker).</summary>
    public static DateOnly Today(this TimeProvider time) => DateOnly.FromDateTime(time.GetLocalNow().DateTime);
}
