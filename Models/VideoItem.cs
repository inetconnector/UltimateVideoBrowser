using System.ComponentModel;
using System.Runtime.CompilerServices;
using SQLite;

namespace UltimateVideoBrowser.Models;

public class VideoItem : INotifyPropertyChanged
{
    private string? thumbnailPath;

    [PrimaryKey] public string Path { get; set; } = "";

    [Indexed] public string Name { get; set; } = "";

    public long DurationMs { get; set; }
    public long DateAddedSeconds { get; set; }

    [Indexed] public string? SourceId { get; set; }

    public string? ThumbnailPath
    {
        get => thumbnailPath;
        set
        {
            if (thumbnailPath == value)
                return;

            thumbnailPath = value;
            OnPropertyChanged();
        }
    }

    [Ignore]
    public string DurationText => DurationMs <= 0 ? "" : TimeSpan.FromMilliseconds(DurationMs).ToString(@"hh\:mm\:ss");

    [Ignore]
    public string FirstLetter
        => string.IsNullOrWhiteSpace(Name) ? "#" : Name.Trim().Substring(0, 1).ToUpperInvariant();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
