using System.Globalization;

namespace UltimateVideoBrowser.Models;

public class TimelineEntry
{
    public TimelineEntry(int year, int month, VideoItem anchorVideo, bool showYear)
    {
        Year = year;
        Month = month;
        AnchorVideo = anchorVideo;
        ShowYear = showYear;

        var date = new DateTime(year, month, 1);
        MonthLabel = date.ToString("MMM", CultureInfo.CurrentCulture).ToUpperInvariant();
        YearLabel = year.ToString(CultureInfo.InvariantCulture);
    }

    public int Year { get; }
    public int Month { get; }
    public string MonthLabel { get; }
    public string YearLabel { get; }
    public bool ShowYear { get; }
    public VideoItem AnchorVideo { get; }
}
