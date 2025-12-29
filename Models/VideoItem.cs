using System.ComponentModel;
using System.Runtime.CompilerServices;
using SQLite;

namespace UltimateVideoBrowser.Models;

public class VideoItem : INotifyPropertyChanged
{
    private bool isMarked;
    private string name = "";
    private string path = "";
    private string? thumbnailPath;

    [PrimaryKey]
    public string Path
    {
        get => path;
        set
        {
            if (path == value)
                return;

            path = value;
            OnPropertyChanged();
        }
    }

    [Indexed]
    public string Name
    {
        get => name;
        set
        {
            if (name == value)
                return;

            name = value;
            OnPropertyChanged();
        }
    }

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
    public bool IsMarked
    {
        get => isMarked;
        set
        {
            if (isMarked == value)
                return;

            isMarked = value;
            OnPropertyChanged();
        }
    }

    [Ignore]
    public string FirstLetter
        => string.IsNullOrWhiteSpace(Name) ? "#" : Name.Trim().Substring(0, 1).ToUpperInvariant();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
