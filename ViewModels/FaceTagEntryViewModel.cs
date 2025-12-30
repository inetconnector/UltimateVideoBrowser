using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UltimateVideoBrowser.ViewModels;

/// <summary>
///     Represents a single detected face within a photo.
///     Holds a preview thumbnail (if available) and the editable person name.
/// </summary>
public sealed class FaceTagEntryViewModel : INotifyPropertyChanged
{
    private string name = string.Empty;
    private ImageSource? thumbnail;

    public int FaceIndex { get; init; }

    public int FaceNumber => FaceIndex + 1;

    public ImageSource? Thumbnail
    {
        get => thumbnail;
        set
        {
            if (thumbnail == value)
                return;

            thumbnail = value;
            OnPropertyChanged();
        }
    }

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

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}