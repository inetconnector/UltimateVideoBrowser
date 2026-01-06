namespace UltimateVideoBrowser.Models;

public sealed class TagNavigationContext
{
    public TagNavigationContext(string tagName, MediaItem? mediaItem)
    {
        TagName = tagName;
        MediaItem = mediaItem;
    }

    public string TagName { get; }
    public MediaItem? MediaItem { get; }
}