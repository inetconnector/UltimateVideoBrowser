using System.ComponentModel;
using System.Runtime.CompilerServices;
using SQLite;
using UltimateVideoBrowser.Resources.Strings;

namespace UltimateVideoBrowser.Models;

public class MediaItem : INotifyPropertyChanged
{
    private bool isMarked;
    private string name = "";
    private string path = "";
    private string peopleTagsSummary = "";
    private string peopleTagActionLabel = AppResources.TagPeopleAction;
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

    [Indexed] public MediaType MediaType { get; set; }

    public long DurationMs { get; set; }
    public long DateAddedSeconds { get; set; }

    [Indexed] public string? SourceId { get; set; }

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

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

    [Ignore] public bool HasDuration => DurationMs > 0;

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
    public string PeopleTagsSummary
    {
        get => peopleTagsSummary;
        set
        {
            if (peopleTagsSummary == value)
                return;

            peopleTagsSummary = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPeopleTags));
        }
    }

    [Ignore]
    public string PeopleTagActionLabel
    {
        get => peopleTagActionLabel;
        set
        {
            if (peopleTagActionLabel == value)
                return;

            peopleTagActionLabel = value;
            OnPropertyChanged();
        }
    }

    // Face scan bookkeeping to avoid re-scanning when the model has not changed.
    public string? FaceScanModelKey { get; set; }

    public long FaceScanAtSeconds { get; set; }

    [Ignore]
    public bool HasLocation => Latitude.HasValue && Longitude.HasValue;

    [Ignore]
    public string LocationText
    {
        get
        {
            if (!HasLocation)
                return string.Empty;

            return string.Format("{0:0.0000}, {1:0.0000}", Latitude, Longitude);
        }
    }

    [Ignore] public bool HasPeopleTags => !string.IsNullOrWhiteSpace(PeopleTagsSummary);

    [Ignore]
    public IReadOnlyList<string> PeopleTags
    {
        get
        {
            var raw = PeopleTagsSummary ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
                return Array.Empty<string>();

            // Split and normalize. Keep original casing, but remove duplicates.
            var parts = raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .ToList();

            if (parts.Count == 0)
                return Array.Empty<string>();

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>(parts.Count);
            foreach (var p in parts)
                if (seen.Add(p))
                    result.Add(p);

            return result;
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
