using System.Globalization;
using System.Resources;

namespace UltimateVideoBrowser.Resources.Strings;

public static class AppResources
{
    static readonly ResourceManager ResourceManager = new("UltimateVideoBrowser.Resources.Strings.AppResources", typeof(AppResources).Assembly);

    static string GetString(string key, string fallback)
        => ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? fallback;

    public static string AppTitle => GetString(nameof(AppTitle), "Ultimate Video Browser");
    public static string MainPageTitle => GetString(nameof(MainPageTitle), "Videos");
    public static string SourcesButton => GetString(nameof(SourcesButton), "Sources");
    public static string ReindexButton => GetString(nameof(ReindexButton), "Reindex");
    public static string SearchPlaceholder => GetString(nameof(SearchPlaceholder), "Search videos...");
    public static string SortTitle => GetString(nameof(SortTitle), "Sort");
    public static string SortName => GetString(nameof(SortName), "Name");
    public static string SortDate => GetString(nameof(SortDate), "Date");
    public static string SortDuration => GetString(nameof(SortDuration), "Duration");
    public static string Indexing => GetString(nameof(Indexing), "Indexing...");
    public static string NewItemsFormat => GetString(nameof(NewItemsFormat), "New items: {0}");
    public static string EmptyStateTitle => GetString(nameof(EmptyStateTitle), "No videos yet");
    public static string EmptyStateMessage => GetString(nameof(EmptyStateMessage), "Add a source or refresh to start browsing your library.");
    public static string SourcesPageTitle => GetString(nameof(SourcesPageTitle), "Sources");
    public static string SourcesPageHeader => GetString(nameof(SourcesPageHeader), "Sources (local folders / synced shares)");
    public static string Enabled => GetString(nameof(Enabled), "Enabled");
    public static string Disabled => GetString(nameof(Disabled), "Disabled");
    public static string AllDeviceVideos => GetString(nameof(AllDeviceVideos), "All device videos");
}
