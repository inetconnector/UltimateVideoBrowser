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
    public static string IndexingStatusFormat => GetString(nameof(IndexingStatusFormat), "Indexing {0} • {1}/{2}");
    public static string NewItemsFormat => GetString(nameof(NewItemsFormat), "New items: {0}");
    public static string PermissionTitle => GetString(nameof(PermissionTitle), "Access required");
    public static string PermissionMessage => GetString(nameof(PermissionMessage), "Allow access to your videos so we can index and play them.");
    public static string PermissionOk => GetString(nameof(PermissionOk), "Grant access");
    public static string OkButton => GetString(nameof(OkButton), "OK");
    public static string EmptyStateTitle => GetString(nameof(EmptyStateTitle), "No videos yet");
    public static string EmptyStateMessage => GetString(nameof(EmptyStateMessage), "Add a source or refresh to start browsing your library.");
    public static string SourcesPageTitle => GetString(nameof(SourcesPageTitle), "Sources");
    public static string SourcesPageHeader => GetString(nameof(SourcesPageHeader), "Sources (local folders / synced shares)");
    public static string AddSourceButton => GetString(nameof(AddSourceButton), "Add folder");
    public static string RemoveSourceButton => GetString(nameof(RemoveSourceButton), "Remove");
    public static string RemoveSourceTitle => GetString(nameof(RemoveSourceTitle), "Remove source?");
    public static string RemoveSourceMessage => GetString(nameof(RemoveSourceMessage), "Remove “{0}” from sources?");
    public static string RemoveSourceConfirm => GetString(nameof(RemoveSourceConfirm), "Remove");
    public static string NewSourceTitle => GetString(nameof(NewSourceTitle), "Add source");
    public static string NewSourcePrompt => GetString(nameof(NewSourcePrompt), "Source name");
    public static string NewSourceConfirm => GetString(nameof(NewSourceConfirm), "Add");
    public static string NewSourceCancel => GetString(nameof(NewSourceCancel), "Cancel");
    public static string NewSourceDefaultName => GetString(nameof(NewSourceDefaultName), "New source");
    public static string SourceExistsTitle => GetString(nameof(SourceExistsTitle), "Already added");
    public static string SourceExistsMessage => GetString(nameof(SourceExistsMessage), "This folder is already in your sources.");
    public static string AddPathHelper => GetString(nameof(AddPathHelper), "Have a network share or local path? Add it directly.");
    public static string AddPathButton => GetString(nameof(AddPathButton), "Add path");
    public static string AddPathTitle => GetString(nameof(AddPathTitle), "Add path");
    public static string AddPathPrompt => GetString(nameof(AddPathPrompt), "Enter a local folder or network share path.");
    public static string PathInvalidTitle => GetString(nameof(PathInvalidTitle), "Path not found");
    public static string PathInvalidMessage => GetString(nameof(PathInvalidMessage), "We couldn't find that folder. Please check the path.");
    public static string Enabled => GetString(nameof(Enabled), "Enabled");
    public static string Disabled => GetString(nameof(Disabled), "Disabled");
    public static string AllDeviceVideos => GetString(nameof(AllDeviceVideos), "All device videos");
    public static string DeviceLibraryPath => GetString(nameof(DeviceLibraryPath), "Device media library");
}
