namespace Issuarr.Core.Issues;

public static class IssueStatusCalculator
{
    /// <summary>
    /// Status is derived rather than stored, so it can never drift out of sync with
    /// files on disk, release dates or monitoring.
    /// </summary>
    public static IssueStatus Calculate(Issue issue, bool titleMonitored, DateOnly today, bool inQueue = false)
    {
        if (issue.IssueFileId.HasValue)
        {
            return IssueStatus.Downloaded;
        }

        if (inQueue)
        {
            return IssueStatus.Downloading;
        }

        if (DateOnly.TryParse(issue.ReleaseDate, out var release) && release > today)
        {
            return IssueStatus.Unaired;
        }

        return titleMonitored && issue.Monitored ? IssueStatus.Missing : IssueStatus.Unmonitored;
    }
}
