namespace UltimateVideoBrowser.ViewModels;

public sealed class SortOption
{
    public SortOption(string key, string display)
    {
        Key = key;
        Display = display;
    }

    public string Key { get; }
    public string Display { get; }
}
