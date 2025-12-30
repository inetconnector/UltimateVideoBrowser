using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace UltimateVideoBrowser.Models;

public class TimelineEntry : INotifyPropertyChanged
{
    public TimelineEntry(int year, int month, MediaItem anchorMedia, bool showYear)
    {
        Year = year;
        Month = month;
        AnchorMedia = anchorMedia;
        ShowYear = showYear;

        var date = new DateTime(year, month, 1);
        MonthLabel = date.ToString("MMM", CultureInfo.CurrentCulture).ToUpperInvariant();
        YearLabel = year.ToString(CultureInfo.InvariantCulture);

        AnchorMedia.PropertyChanged += OnAnchorMediaPropertyChanged;
    }

    public int Year { get; }
    public int Month { get; }
    public string MonthLabel { get; }
    public string YearLabel { get; }
    public bool ShowYear { get; }
    public MediaItem AnchorMedia { get; }

    public string? ThumbnailPath => AnchorMedia.ThumbnailPath;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnAnchorMediaPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaItem.ThumbnailPath))
            OnPropertyChanged(nameof(ThumbnailPath));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
