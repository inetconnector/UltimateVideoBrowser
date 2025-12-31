using System.Globalization;
using System.Resources;

namespace UltimateVideoBrowser.Resources.Strings;

public static class AppResources
{
    private static readonly ResourceManager ResourceManager =
        new("UltimateVideoBrowser.Resources.Strings.AppResources", typeof(AppResources).Assembly);

    public static string AppTitle => GetString(nameof(AppTitle), "Ultimate Media Browser");
    public static string MainPageTitle => GetString(nameof(MainPageTitle), "Media");
    public static string SourcesButton => GetString(nameof(SourcesButton), "Sources");
    public static string PeopleButton => GetString(nameof(PeopleButton), "People");
    public static string SettingsButton => GetString(nameof(SettingsButton), "Settings");
    public static string ReindexButton => GetString(nameof(ReindexButton), "Reindex");
    public static string SearchPlaceholder => GetString(nameof(SearchPlaceholder), "Search media...");
    public static string HeroEyebrow => GetString(nameof(HeroEyebrow), "Your personal library");
    public static string HeroTitle => GetString(nameof(HeroTitle), "Discover every file instantly");

    public static string HeroSubtitle => GetString(nameof(HeroSubtitle),
        "Browse with rich thumbnails, smart search, and fast indexing across all your folders.");

    public static string HeroPrimaryAction => GetString(nameof(HeroPrimaryAction), "Manage sources");
    public static string BackButton => GetString(nameof(BackButton), "Back");
    public static string SortTitle => GetString(nameof(SortTitle), "Sort");
    public static string SortName => GetString(nameof(SortName), "Name");
    public static string SortDate => GetString(nameof(SortDate), "Date");
    public static string SortDuration => GetString(nameof(SortDuration), "Duration");
    public static string DateFilterTitle => GetString(nameof(DateFilterTitle), "Date");
    public static string DateFilterFromLabel => GetString(nameof(DateFilterFromLabel), "From");
    public static string DateFilterToLabel => GetString(nameof(DateFilterToLabel), "To");
    public static string Indexing => GetString(nameof(Indexing), "Indexing...");
    public static string IndexingStatusFormat => GetString(nameof(IndexingStatusFormat), "Indexing {0} • {1}/{2}");
    public static string IndexingFolderFormat => GetString(nameof(IndexingFolderFormat), "Folder: {0}");
    public static string IndexingFileFormat => GetString(nameof(IndexingFileFormat), "File: {0}");

    public static string IndexingShowDetailsButton =>
        GetString(nameof(IndexingShowDetailsButton), "Show progress");

    public static string IndexingBackgroundButton =>
        GetString(nameof(IndexingBackgroundButton), "Hide");

    public static string IndexingCancelButton =>
        GetString(nameof(IndexingCancelButton), "Cancel");

    public static string NewItemsFormat => GetString(nameof(NewItemsFormat), "New items: {0}");
    public static string HighlightsTitle => GetString(nameof(HighlightsTitle), "Library highlights");
    public static string HighlightsVideosLabel => GetString(nameof(HighlightsVideosLabel), "Media indexed");
    public static string HighlightsSourcesLabel => GetString(nameof(HighlightsSourcesLabel), "Sources enabled");
    public static string HighlightsTipLabel => GetString(nameof(HighlightsTipLabel), "Pro tip");
    public static string HighlightsPeopleTaggedLabel =>
        GetString(nameof(HighlightsPeopleTaggedLabel), "People tagged");

    public static string HighlightsTipText =>
        GetString(nameof(HighlightsTipText),
            "Use search + filters, then reindex after adding new folders for the best results.");

    public static string SourcesSummaryFormat => GetString(nameof(SourcesSummaryFormat), "{0} of {1} sources active");
    public static string PermissionTitle => GetString(nameof(PermissionTitle), "Access required");

    public static string PermissionMessage => GetString(nameof(PermissionMessage),
        "Allow access to your media so we can index and open it.");

    public static string PermissionOk => GetString(nameof(PermissionOk), "Grant access");
    public static string OkButton => GetString(nameof(OkButton), "OK");
    public static string ShareAction => GetString(nameof(ShareAction), "Share");
    public static string ShareTitle => GetString(nameof(ShareTitle), "Share file");
    public static string SaveAsAction => GetString(nameof(SaveAsAction), "Save as...");
    public static string SaveAsFileTypeLabel => GetString(nameof(SaveAsFileTypeLabel), "File");
    public static string SaveAsFailedTitle => GetString(nameof(SaveAsFailedTitle), "Save failed");

    public static string SaveAsFailedMessage =>
        GetString(nameof(SaveAsFailedMessage), "We couldn't save this file. Please try a different location.");

    public static string SaveAsNotSupportedMessage =>
        GetString(nameof(SaveAsNotSupportedMessage), "Save as isn't supported on this device.");

    public static string RenameAction => GetString(nameof(RenameAction), "Rename");
    public static string RenameTitle => GetString(nameof(RenameTitle), "Rename file");
    public static string RenameMessage => GetString(nameof(RenameMessage), "Enter a new name for this file.");
    public static string RenamePlaceholder => GetString(nameof(RenamePlaceholder), "File name");
    public static string RenameFailedTitle => GetString(nameof(RenameFailedTitle), "Rename failed");

    public static string RenameFailedMessage =>
        GetString(nameof(RenameFailedMessage), "We couldn't rename this file. Please try again.");

    public static string RenameNotSupportedMessage =>
        GetString(nameof(RenameNotSupportedMessage), "Renaming isn't supported on this device.");

    public static string RenameExistsMessage =>
        GetString(nameof(RenameExistsMessage), "A file with that name already exists in this folder.");

    public static string OpenFolderAction => GetString(nameof(OpenFolderAction), "Open folder");
    public static string OpenFolderFailedTitle => GetString(nameof(OpenFolderFailedTitle), "Open folder failed");

    public static string OpenFolderFailedMessage =>
        GetString(nameof(OpenFolderFailedMessage), "We couldn't open this folder. Please try again.");

    public static string OpenFolderNotSupportedMessage =>
        GetString(nameof(OpenFolderNotSupportedMessage), "Opening folders isn't supported on this device.");

    public static string MarkedCountFormat => GetString(nameof(MarkedCountFormat), "Marked: {0}");
    public static string CopyMarkedAction => GetString(nameof(CopyMarkedAction), "Copy");
    public static string MoveMarkedAction => GetString(nameof(MoveMarkedAction), "Move");
    public static string ClearMarkedAction => GetString(nameof(ClearMarkedAction), "Clear");
    public static string DeleteMarkedAction => GetString(nameof(DeleteMarkedAction), "Delete permanently");
    public static string DeleteConfirmTitle => GetString(nameof(DeleteConfirmTitle), "Delete from disk?");

    public static string DeleteConfirmMessageFormat =>
        GetString(nameof(DeleteConfirmMessageFormat), "Delete {0} item(s) permanently? This cannot be undone.");

    public static string DeleteCompletedTitle => GetString(nameof(DeleteCompletedTitle), "Delete completed");

    public static string DeleteCompletedMessageFormat =>
        GetString(nameof(DeleteCompletedMessageFormat), "Deleted: {0}, failed: {1}.");

    public static string DeleteFailedTitle => GetString(nameof(DeleteFailedTitle), "Delete failed");

    public static string DeleteNotSupportedMessage =>
        GetString(nameof(DeleteNotSupportedMessage), "Permanent delete isn't supported on this device.");

    public static string TransferFolderTitle => GetString(nameof(TransferFolderTitle), "Create destination folder");

    public static string TransferFolderMessage =>
        GetString(nameof(TransferFolderMessage), "Choose a name for the new folder.");

    public static string TransferFolderPlaceholder => GetString(nameof(TransferFolderPlaceholder), "Folder name");
    public static string CreateButton => GetString(nameof(CreateButton), "Create");
    public static string CancelButton => GetString(nameof(CancelButton), "Cancel");
    public static string TransferFailedTitle => GetString(nameof(TransferFailedTitle), "Transfer failed");

    public static string TransferFailedMessage =>
        GetString(nameof(TransferFailedMessage), "We couldn't copy or move the selected files.");

    public static string TransferNotSupportedMessage =>
        GetString(nameof(TransferNotSupportedMessage), "Copy or move isn't supported on this device.");

    public static string TransferCopyCompletedTitle => GetString(nameof(TransferCopyCompletedTitle), "Copy complete");
    public static string TransferMoveCompletedTitle => GetString(nameof(TransferMoveCompletedTitle), "Move complete");

    public static string TransferCompletedMessageFormat =>
        GetString(nameof(TransferCompletedMessageFormat), "Successful: {0}, skipped: {1}, failed: {2}.");

    public static string EmptyStateTitle => GetString(nameof(EmptyStateTitle), "No items yet");

    public static string EmptyStateMessage => GetString(nameof(EmptyStateMessage),
        "Add a source or refresh to start browsing your library.");

    public static string EmptyStateAction => GetString(nameof(EmptyStateAction), "Add sources");
    public static string SettingsPageTitle => GetString(nameof(SettingsPageTitle), "Settings");
    public static string SettingsPageHeader => GetString(nameof(SettingsPageHeader), "Settings");
    public static string SettingsAppearanceTitle => GetString(nameof(SettingsAppearanceTitle), "Appearance");
    public static string SettingsThemeLabel => GetString(nameof(SettingsThemeLabel), "Theme");
    public static string SettingsSortingTitle => GetString(nameof(SettingsSortingTitle), "Sorting");
    public static string SettingsDefaultSortLabel => GetString(nameof(SettingsDefaultSortLabel), "Default order");
    public static string SettingsFilterTitle => GetString(nameof(SettingsFilterTitle), "Filter");
    public static string SettingsDateFilterLabel => GetString(nameof(SettingsDateFilterLabel), "Enable date filter");
    public static string SettingsDateFromLabel => GetString(nameof(SettingsDateFromLabel), "From");
    public static string SettingsDateToLabel => GetString(nameof(SettingsDateToLabel), "To");
    public static string SettingsIndexingTitle => GetString(nameof(SettingsIndexingTitle), "Indexing");
    public static string SettingsReindexLabel => GetString(nameof(SettingsReindexLabel), "Re-index on next launch");
    public static string SettingsPlaybackTitle => GetString(nameof(SettingsPlaybackTitle), "Playback");

    public static string SettingsInternalPlayerLabel =>
        GetString(nameof(SettingsInternalPlayerLabel), "Use internal video player for videos");

    public static string SettingsFileChangesTitle =>
        GetString(nameof(SettingsFileChangesTitle), "File changes");

    public static string SettingsFileChangesLabel =>
        GetString(nameof(SettingsFileChangesLabel), "Allow renaming, moving, copying, and deleting");

    public static string SettingsFileChangesHint =>
        GetString(nameof(SettingsFileChangesHint), "Enable this to show actions that change files on disk.");

    public static string SettingsPeopleTaggingTitle =>
        GetString(nameof(SettingsPeopleTaggingTitle), "People tagging");

    public static string SettingsPeopleTaggingLabel =>
        GetString(nameof(SettingsPeopleTaggingLabel), "Enable people tagging");

    public static string SettingsPeopleTaggingHint =>
        GetString(nameof(SettingsPeopleTaggingHint),
            "Turn this on to tag people in photos and videos from the library.");

        public static string SettingsPeopleModelsFolderFormat =>
        GetString(nameof(SettingsPeopleModelsFolderFormat), "Folder: {0}");

    public static string SettingsPeopleModelsFileFormat =>
        GetString(nameof(SettingsPeopleModelsFileFormat), "File: {0}");

    public static string SettingsPeopleModelsMissingDetailFormat =>
        GetString(nameof(SettingsPeopleModelsMissingDetailFormat), "Missing: {0}");

public static string SettingsPeopleModelsStatusLabel =>
        GetString(nameof(SettingsPeopleModelsStatusLabel), "Face models");

    public static string SettingsPeopleModelsStatusReady =>
        GetString(nameof(SettingsPeopleModelsStatusReady), "Ready");

    public static string SettingsPeopleModelsStatusMissing =>
        GetString(nameof(SettingsPeopleModelsStatusMissing), "Missing");

    public static string SettingsPeopleModelsStatusDownloading =>
        GetString(nameof(SettingsPeopleModelsStatusDownloading), "Downloading...");
     
    public static string TagPeopleAction =>
        GetString(nameof(TagPeopleAction), "Tag people");

    public static string TagPeopleTitle =>
        GetString(nameof(TagPeopleTitle), "Tag people");

    public static string TagPeopleMessage =>
        GetString(nameof(TagPeopleMessage), "Enter names separated by commas.");

    public static string TagPeoplePlaceholder =>
        GetString(nameof(TagPeoplePlaceholder), "e.g. Alex, Sam, Priya");

    public static string RefreshButton => GetString(nameof(RefreshButton), "Refresh");
    public static string SaveButton => GetString(nameof(SaveButton), "Save");
    public static string OpenButton => GetString(nameof(OpenButton), "Open");

    public static string PeoplePageTitle => GetString(nameof(PeoplePageTitle), "People");
    public static string PeoplePageHeader => GetString(nameof(PeoplePageHeader), "People");
    public static string PeopleSearchPlaceholder => GetString(nameof(PeopleSearchPlaceholder), "Search people...");

    public static string PeopleEmptyHint =>
        GetString(nameof(PeopleEmptyHint), "No people yet. Enable people tagging and index photos to see them here.");

    public static string TaggedPhotosButton => GetString(nameof(TaggedPhotosButton), "Tagged photos");
    public static string TaggedPhotosPageTitle => GetString(nameof(TaggedPhotosPageTitle), "Tagged photos");
    public static string TaggedPhotosPageHeader => GetString(nameof(TaggedPhotosPageHeader), "Tagged photos");

    public static string TaggedPhotosSearchPlaceholder =>
        GetString(nameof(TaggedPhotosSearchPlaceholder), "Search by person name...");

    public static string TaggedPhotosEmptyHint =>
        GetString(nameof(TaggedPhotosEmptyHint), "No tagged photos yet. Tag people in a photo to see it here.");

    public static string PersonPageTitle => GetString(nameof(PersonPageTitle), "Person");
    public static string PersonNamePlaceholder => GetString(nameof(PersonNamePlaceholder), "Name");
    public static string PersonEmptyHint => GetString(nameof(PersonEmptyHint), "No photos yet.");

    public static string TagPeopleEditorTitle => GetString(nameof(TagPeopleEditorTitle), "Tag people");
    public static string TagPeopleEditorHeader => GetString(nameof(TagPeopleEditorHeader), "Name faces");

    public static string TagPeopleEditorTagsLabel => GetString(nameof(TagPeopleEditorTagsLabel), "People tags");

    public static string TagPeopleEditorTagsPlaceholder =>
        GetString(nameof(TagPeopleEditorTagsPlaceholder), "Add names, separated by commas...");

    public static string TagPeopleEditorTagsHint =>
        GetString(nameof(TagPeopleEditorTagsHint), "Tip: You can tag a photo even if no faces were detected.");

    public static string TagPeopleEditorEmptyHint =>
        GetString(nameof(TagPeopleEditorEmptyHint), "No faces detected for this photo.");

    public static string PreviewTitle => GetString(nameof(PreviewTitle), "Preview");

    public static string PreviewEmptyMessage =>
        GetString(nameof(PreviewEmptyMessage), "Select a file to preview it here.");

    public static string LoadingMedia => GetString(nameof(LoadingMedia), "Loading media...");
    public static string SourcesPageTitle => GetString(nameof(SourcesPageTitle), "Sources");

    public static string SourcesPageHeader =>
        GetString(nameof(SourcesPageHeader), "Sources (local folders / synced shares)");

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

    public static string SourceExistsMessage =>
        GetString(nameof(SourceExistsMessage), "This folder is already in your sources.");

    public static string AddPathHelper =>
        GetString(nameof(AddPathHelper), "Have a network share or local path? Add it directly.");

    public static string AddPathButton => GetString(nameof(AddPathButton), "Add path");
    public static string AddPathTitle => GetString(nameof(AddPathTitle), "Add path");

    public static string AddPathPrompt =>
        GetString(nameof(AddPathPrompt), "Enter a local folder or network share path.");

    public static string AddAnotherFolderTitle => GetString(nameof(AddAnotherFolderTitle), "Add another folder?");

    public static string AddAnotherFolderMessage =>
        GetString(nameof(AddAnotherFolderMessage), "Would you like to add another folder?");

    public static string AddAnotherFolderConfirm => GetString(nameof(AddAnotherFolderConfirm), "Add another");
    public static string AddAnotherFolderCancel => GetString(nameof(AddAnotherFolderCancel), "Done");

    public static string PathInvalidTitle => GetString(nameof(PathInvalidTitle), "Path not found");

    public static string PathInvalidMessage => GetString(nameof(PathInvalidMessage),
        "We couldn't find that folder. Please check the path.");

    public static string Enabled => GetString(nameof(Enabled), "Enabled");
    public static string Disabled => GetString(nameof(Disabled), "Disabled");
    public static string ThemeLight => GetString(nameof(ThemeLight), "Light");
    public static string ThemeDark => GetString(nameof(ThemeDark), "Dark");
    public static string AllDeviceVideos => GetString(nameof(AllDeviceVideos), "All device media");
    public static string DeviceLibraryPath => GetString(nameof(DeviceLibraryPath), "Device media library");
    public static string MediaTypeVideos => GetString(nameof(MediaTypeVideos), "Videos");
    public static string MediaTypePhotos => GetString(nameof(MediaTypePhotos), "Photos");
    public static string MediaTypeDocuments => GetString(nameof(MediaTypeDocuments), "Documents");
    public static string MediaTypeFilterTitle => GetString(nameof(MediaTypeFilterTitle), "Media types");
    public static string SettingsMediaTypesTitle => GetString(nameof(SettingsMediaTypesTitle), "Media types");

    public static string SettingsMediaTypesMessage => GetString(nameof(SettingsMediaTypesMessage),
        "Choose which types to index in your library.");

    public static string SettingsExtensionsTitle => GetString(nameof(SettingsExtensionsTitle), "File extensions");

    public static string SettingsExtensionsHint => GetString(nameof(SettingsExtensionsHint),
        "Separate extensions with commas (e.g. .mp4, .mkv).");

    public static string SettingsExtensionsPlaceholder =>
        GetString(nameof(SettingsExtensionsPlaceholder), ".mp4, .mkv, .avi");

    public static string RenameConfirmTitle => GetString(nameof(RenameConfirmTitle), "Confirm rename");

    public static string RenameConfirmMessage =>
        GetString(nameof(RenameConfirmMessage), "Rename \"{0}\" to \"{1}\"?");

    private static string GetString(string key, string fallback)
    {
        return ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? fallback;
    }
}