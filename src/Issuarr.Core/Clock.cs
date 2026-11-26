namespace Issuarr.Core;

public interface IClock
{
    DateOnly Today { get; }
    DateTime UtcNow { get; }
}

public class SystemClock : IClock
{
    public DateOnly Today => DateOnly.FromDateTime(DateTime.Now);
    public DateTime UtcNow => DateTime.UtcNow;
}
