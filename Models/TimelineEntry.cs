using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace UltimateVideoBrowser.Models;

public class TimelineEntry : INotifyPropertyChanged
{
    private readonly VideoItem anchorVideo;

    public TimelineEntry(int year, int month, VideoItem anchorVideo, bool showYear)
    {
        Year = year;
        Month = month;
        this.anchorVideo = anchorVideo;
        ShowYear = showYear;

        var date = new DateTime(year, month, 1);
        MonthLabel = date.ToString("MMM", CultureInfo.CurrentCulture).ToUpperInvariant();
        YearLabel = year.ToString(CultureInfo.InvariantCulture);

        this.anchorVideo.PropertyChanged += OnAnchorVideoPropertyChanged;
    }

    public int Year { get; }
    public int Month { get; }
    public string MonthLabel { get; }
    public string YearLabel { get; }
    public bool ShowYear { get; }
    public VideoItem AnchorVideo => anchorVideo;
    public string? ThumbnailPath => anchorVideo.ThumbnailPath;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnAnchorVideoPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(VideoItem.ThumbnailPath))
            OnPropertyChanged(nameof(ThumbnailPath));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
