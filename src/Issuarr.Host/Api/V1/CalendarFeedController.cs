using System.Text;
using Issuarr.Core.Issues;
using Issuarr.Core.Titles;
using Microsoft.AspNetCore.Mvc;

namespace Issuarr.Host.Api.V1;

/// <summary>iCal feed of releases for calendar apps: /feed/v1/calendar/issuarr.ics?apikey=…</summary>
[ApiController]
[Route("feed/v1/calendar")]
public class CalendarFeedController(IIssueRepository issues, ITitleService titles) : ControllerBase
{
    [HttpGet("issuarr.ics")]
    public IActionResult Get([FromQuery] int pastDays = 28, [FromQuery] int futureDays = 90, [FromQuery] bool unmonitored = false)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var titleMap = titles.All().ToDictionary(t => t.Id);
        var sb = new StringBuilder();
        sb.Append("BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Issuarr//Issuarr//EN\r\nCALSCALE:GREGORIAN\r\nX-WR-CALNAME:Issuarr\r\n");
        foreach (var i in issues.Between(today.AddDays(-pastDays).ToString("yyyy-MM-dd"), today.AddDays(futureDays).ToString("yyyy-MM-dd")))
        {
            if (!titleMap.TryGetValue(i.TitleId, out var t) || (!unmonitored && (!t.Monitored || !i.Monitored)))
            {
                continue;
            }

            var date = DateOnly.Parse(i.ReleaseDate);
            sb.Append("BEGIN:VEVENT\r\n");
            sb.Append($"UID:issuarr-issue-{i.Id}\r\n");
            sb.Append($"DTSTAMP:{DateTime.UtcNow:yyyyMMddTHHmmssZ}\r\n");
            sb.Append($"DTSTART;VALUE=DATE:{date:yyyyMMdd}\r\n");
            sb.Append($"DTEND;VALUE=DATE:{date.AddDays(1):yyyyMMdd}\r\n");
            sb.Append($"SUMMARY:{Escape($"{t.Name} {i.Number}")}\r\n");
            if (i.IssueTitle != null)
            {
                sb.Append($"DESCRIPTION:{Escape(i.IssueTitle)}\r\n");
            }

            sb.Append($"STATUS:{(i.IssueFileId != null ? "CONFIRMED" : "TENTATIVE")}\r\n");
            sb.Append("END:VEVENT\r\n");
        }

        sb.Append("END:VCALENDAR\r\n");
        return File(Encoding.UTF8.GetBytes(sb.ToString()), "text/calendar; charset=utf-8", "issuarr.ics");
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");
}
