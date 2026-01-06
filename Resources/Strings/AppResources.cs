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
    public static string AlbumsButton => GetString(nameof(AlbumsButton), "Albums");
    public static string PeopleButton => GetString(nameof(PeopleButton), "People");
    public static string MergeIntoTitle => GetString(nameof(MergeIntoTitle), "Merge into…");
    public static string MergeButton => GetString(nameof(MergeButton), "Merge");
    public static string SettingsButton => GetString(nameof(SettingsButton), "Settings");
    public static string ReindexButton => GetString(nameof(ReindexButton), "Reindex");
	public static string ReindexTitle => GetString(nameof(ReindexTitle), "Reindex library");
	public static string ReindexPrompt => GetString(nameof(ReindexPrompt), "Do you want to reindex everything now? This may take a while depending on your library size.");

	public static string PeopleTagsTrialTitle => GetString(nameof(PeopleTagsTrialTitle), "People tagging trial");
	public static string PeopleTagsTrialHint => GetString(nameof(PeopleTagsTrialHint), "People tags are enabled for 14 days. Upgrade to Pro to keep them always available.");
	public static string UpgradeNowButton => GetString(nameof(UpgradeNowButton), "Upgrade now");

    // Database backup / restore
    public static string BackupSectionTitle => GetString(nameof(BackupSectionTitle), "Database backup");
    public static string BackupSectionHint => GetString(nameof(BackupSectionHint), "Export or import the local database including settings and thumbnails.");
    public static string BackupExportButton => GetString(nameof(BackupExportButton), "Export backup");
    public static string BackupImportButton => GetString(nameof(BackupImportButton), "Import backup");
    public static string BackupImportPickerTitle => GetString(nameof(BackupImportPickerTitle), "Select a backup ZIP");
    public static string BackupExportSuccessTitle => GetString(nameof(BackupExportSuccessTitle), "Backup exported");
    public static string BackupExportSuccessMessage => GetString(nameof(BackupExportSuccessMessage), "Your backup was exported successfully.");
    public static string BackupExportFailedTitle => GetString(nameof(BackupExportFailedTitle), "Backup export failed");
    public static string BackupExportFailedMessage => GetString(nameof(BackupExportFailedMessage), "The backup could not be created or saved.");
    public static string BackupImportNotAllowedTitle => GetString(nameof(BackupImportNotAllowedTitle), "Indexing in progress");
    public static string BackupImportNotAllowedMessage => GetString(nameof(BackupImportNotAllowedMessage), "Stop indexing before restoring a backup.");
    public static string BackupImportConfirmTitle => GetString(nameof(BackupImportConfirmTitle), "Restore backup?");
    public static string BackupImportConfirmMessage => GetString(nameof(BackupImportConfirmMessage), "This will overwrite your current database, settings and thumbnails. Continue?");
    public static string RestoreButton => GetString(nameof(RestoreButton), "Restore");
    public static string BackupImportMissingDbMessage => GetString(nameof(BackupImportMissingDbMessage), "This backup does not contain a database file.");
    public static string BackupImportSuccessTitle => GetString(nameof(BackupImportSuccessTitle), "Restore completed");
    public static string BackupImportSuccessMessage => GetString(nameof(BackupImportSuccessMessage), "Backup restored. You may need to reopen the app to see all changes.");
    public static string BackupImportFailedTitle => GetString(nameof(BackupImportFailedTitle), "Restore failed");
    public static string BackupImportFailedMessage => GetString(nameof(BackupImportFailedMessage), "The backup could not be restored.");
    public static string ActionsButton => GetString(nameof(ActionsButton), "Actions");
    public static string SearchPlaceholder => GetString(nameof(SearchPlaceholder), "Search media...");
    public static string SearchScopeTitle => GetString(nameof(SearchScopeTitle), "Search in");
    public static string SearchScopeName => GetString(nameof(SearchScopeName), "Titles");
    public static string SearchScopePeople => GetString(nameof(SearchScopePeople), "People/Tags");
    public static string SearchScopeAlbums => GetString(nameof(SearchScopeAlbums), "Albums");
    public static string HeroEyebrow => GetString(nameof(HeroEyebrow), "Your personal library");
    public static string HeroTitle => GetString(nameof(HeroTitle), "Discover every file instantly");

    public static string HeroSubtitle => GetString(nameof(HeroSubtitle),
        "Browse with rich thumbnails, smart search, and fast indexing across all your folders.");

    public static string HeroPrimaryAction => GetString(nameof(HeroPrimaryAction), "Manage sources");
    public static string BackButton => GetString(nameof(BackButton), "Back");
    public static string AlbumsHeader => GetString(nameof(AlbumsHeader), "Albums");
    public static string ManageAlbumsButton => GetString(nameof(ManageAlbumsButton), "Manage");
    public static string AlbumsPageTitle => GetString(nameof(AlbumsPageTitle), "Albums");
    public static string AlbumsPageHeader => GetString(nameof(AlbumsPageHeader), "Manage albums");
    public static string AddAlbumButton => GetString(nameof(AddAlbumButton), "Add album");
    public static string AlbumItemCountFormat => GetString(nameof(AlbumItemCountFormat), "{0} items");
    public static string AllAlbumsTab => GetString(nameof(AllAlbumsTab), "All media");
    public static string AddToAlbumAction => GetString(nameof(AddToAlbumAction), "Add to album");
    public static string NewAlbumAction => GetString(nameof(NewAlbumAction), "New album…");
    public static string NewAlbumTitle => GetString(nameof(NewAlbumTitle), "New album");
    public static string NewAlbumPrompt => GetString(nameof(NewAlbumPrompt), "Enter a name for the album.");
    public static string NewAlbumConfirm => GetString(nameof(NewAlbumConfirm), "Create");
    public static string NewAlbumPlaceholder => GetString(nameof(NewAlbumPlaceholder), "Album name");
    public static string AlbumExistsTitle => GetString(nameof(AlbumExistsTitle), "Album already exists");

    public static string AlbumExistsMessage =>
        GetString(nameof(AlbumExistsMessage), "An album named \"{0}\" already exists.");

    public static string RenameAlbumTitle => GetString(nameof(RenameAlbumTitle), "Rename album");
    public static string RenameAlbumPrompt => GetString(nameof(RenameAlbumPrompt), "Choose a new name for this album.");
    public static string RenameAlbumConfirm => GetString(nameof(RenameAlbumConfirm), "Rename");
    public static string RenameAlbumAction => GetString(nameof(RenameAlbumAction), "Rename");
    public static string DeleteAlbumTitle => GetString(nameof(DeleteAlbumTitle), "Delete album");

    public static string DeleteAlbumMessage =>
        GetString(nameof(DeleteAlbumMessage), "Delete the album \"{0}\"? The media items remain in your library.");

    public static string DeleteAlbumConfirm => GetString(nameof(DeleteAlbumConfirm), "Delete");
    public static string DeleteAlbumAction => GetString(nameof(DeleteAlbumAction), "Delete");
    public static string SortTitle => GetString(nameof(SortTitle), "Sort");
    public static string SortName => GetString(nameof(SortName), "Name");
    public static string SortDate => GetString(nameof(SortDate), "Date");
    public static string SortDuration => GetString(nameof(SortDuration), "Duration");
    public static string DateFilterTitle => GetString(nameof(DateFilterTitle), "Date");
    public static string DateFilterFromLabel => GetString(nameof(DateFilterFromLabel), "From");
    public static string DateFilterToLabel => GetString(nameof(DateFilterToLabel), "To");
    public static string Indexing => GetString(nameof(Indexing), "Indexing...");
    public static string IndexStatusRunningTitle => GetString(nameof(IndexStatusRunningTitle), "Index running");

    public static string IndexStatusRunningMessage => GetString(nameof(IndexStatusRunningMessage),
        "Your library is being indexed. Progress details are available.");

    public static string IndexStatusReadyTitle => GetString(nameof(IndexStatusReadyTitle), "Index up to date");

    public static string IndexStatusReadyMessage =>
        GetString(nameof(IndexStatusReadyMessage), "No indexing is needed right now.");

    public static string IndexStatusNeededTitle => GetString(nameof(IndexStatusNeededTitle), "Index needs update");

    public static string IndexStatusNeededMessage =>
        GetString(nameof(IndexStatusNeededMessage), "Sources changed. Run indexing to refresh your library.");

    public static string IndexStatusActionStart => GetString(nameof(IndexStatusActionStart), "Start indexing");
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

    public static string ErrorLogTitle => GetString(nameof(ErrorLogTitle), "Error log");
    public static string ErrorLogHint =>
        GetString(nameof(ErrorLogHint), "Share this log if something fails or a thumbnail is blank.");

    public static string ErrorLogShowButton => GetString(nameof(ErrorLogShowButton), "Show log");
    public static string ErrorLogCopyButton => GetString(nameof(ErrorLogCopyButton), "Copy log");
    public static string ErrorLogShareButton => GetString(nameof(ErrorLogShareButton), "Share log");
    public static string ErrorLogClearButton => GetString(nameof(ErrorLogClearButton), "Clear log");
    public static string ErrorLogEmptyMessage => GetString(nameof(ErrorLogEmptyMessage), "No errors logged yet.");
    public static string ErrorLogShareTitle => GetString(nameof(ErrorLogShareTitle), "Share error log");
    public static string ErrorLogCopiedMessage => GetString(nameof(ErrorLogCopiedMessage), "Error log copied.");
	public static string ErrorLogCopyFailedMessage => GetString(nameof(ErrorLogCopyFailedMessage), "Could not copy the error log to the clipboard. Please try again.");

    public static string ErrorLogClearedMessage =>
        GetString(nameof(ErrorLogClearedMessage), "The error log has been cleared.");

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
    public static string OpenLocationAction => GetString(nameof(OpenLocationAction), "Location");
    public static string OpenFolderFailedTitle => GetString(nameof(OpenFolderFailedTitle), "Open folder failed");

    public static string OpenLocationFailedTitle =>
        GetString(nameof(OpenLocationFailedTitle), "Open location failed");

    public static string OpenFolderFailedMessage =>
        GetString(nameof(OpenFolderFailedMessage), "We couldn't open this folder. Please try again.");

    public static string OpenFolderNotSupportedMessage =>
        GetString(nameof(OpenFolderNotSupportedMessage), "Opening folders isn't supported on this device.");

    public static string OpenLocationFailedMessage =>
        GetString(nameof(OpenLocationFailedMessage), "We couldn't open this location. Please try again.");

    public static string OpenLocationNotSupportedMessage =>
        GetString(nameof(OpenLocationNotSupportedMessage), "Opening locations isn't supported on this device.");

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
    public static string SettingsIndexingStatusLabel => GetString(nameof(SettingsIndexingStatusLabel), "Status");
    public static string SettingsReindexLabel => GetString(nameof(SettingsReindexLabel), "Re-index on next launch");

    public static string SettingsDatabaseResetHint =>
        GetString(nameof(SettingsDatabaseResetHint),
            "Deletes the local database and rebuilds it by rescanning your sources.");

    public static string SettingsDatabaseResetButton =>
        GetString(nameof(SettingsDatabaseResetButton), "Delete database");

    public static string SettingsDatabaseResetConfirmTitle =>
        GetString(nameof(SettingsDatabaseResetConfirmTitle), "Delete database?");

    public static string SettingsDatabaseResetConfirmMessage =>
        GetString(nameof(SettingsDatabaseResetConfirmMessage),
            "This removes all indexed data and tags. The app will rescan your sources.");

    public static string SettingsDatabaseResetConfirmAccept =>
        GetString(nameof(SettingsDatabaseResetConfirmAccept), "Delete");

    public static string SettingsDatabaseResetKeepSourcesTitle =>
        GetString(nameof(SettingsDatabaseResetKeepSourcesTitle), "Keep sources?");

    public static string SettingsDatabaseResetKeepSourcesMessage =>
        GetString(nameof(SettingsDatabaseResetKeepSourcesMessage),
            "Do you want to keep your current sources for the rescan?");

    public static string SettingsDatabaseResetKeepSourcesAccept =>
        GetString(nameof(SettingsDatabaseResetKeepSourcesAccept), "Keep sources");

    public static string SettingsDatabaseResetKeepSourcesRemove =>
        GetString(nameof(SettingsDatabaseResetKeepSourcesRemove), "Remove sources");

    public static string SettingsDatabaseResetCompletedTitle =>
        GetString(nameof(SettingsDatabaseResetCompletedTitle), "Database deleted");

    public static string SettingsDatabaseResetCompletedMessage =>
        GetString(nameof(SettingsDatabaseResetCompletedMessage),
            "Rescanning has started and the database will be rebuilt.");

    public static string SettingsDatabaseResetFailedTitle =>
        GetString(nameof(SettingsDatabaseResetFailedTitle), "Delete failed");

    public static string SettingsDatabaseResetFailedMessage =>
        GetString(nameof(SettingsDatabaseResetFailedMessage),
            "The database could not be rebuilt. Please try again.");

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

    public static string SettingsProTitle => GetString(nameof(SettingsProTitle), "Pro upgrade");
    public static string SettingsProStatusFreeTitle => GetString(nameof(SettingsProStatusFreeTitle), "Free");

    public static string SettingsProStatusUnlockedTitle =>
        GetString(nameof(SettingsProStatusUnlockedTitle), "Pro unlocked");

    public static string SettingsProFreeMessage =>
        GetString(nameof(SettingsProFreeMessage), "Unlock unlimited face recognition and automation.");

    public static string SettingsProUnlockedMessage =>
        GetString(nameof(SettingsProUnlockedMessage), "Thanks for supporting local-first photo organization.");

    public static string SettingsProFeatureList =>
        GetString(nameof(SettingsProFeatureList),
            "• Unlimited face recognition\n• Automatic people albums\n• Batch sorting (1000+ photos)\n• Local export & backups\n• No ads");

    public static string SettingsProPriceFormat =>
        GetString(nameof(SettingsProPriceFormat), "One-time PayPal checkout: {0} (via license server)");

    public static string SettingsProPriceFallback => GetString(nameof(SettingsProPriceFallback), "3,92 €");
    public static string SettingsProUnlockButton => GetString(nameof(SettingsProUnlockButton), "Unlock Pro");
    public static string SettingsProRestoreButton => GetString(nameof(SettingsProRestoreButton), "Restore purchase");

    public static string SettingsProLimitReachedMessage =>
        GetString(nameof(SettingsProLimitReachedMessage),
            "Free limit reached ({0} people). Upgrade to Pro for unlimited.");

    public static string SettingsProPurchaseSuccessTitle =>
        GetString(nameof(SettingsProPurchaseSuccessTitle), "Pro unlocked");

    public static string SettingsProPurchaseSuccessMessage =>
        GetString(nameof(SettingsProPurchaseSuccessMessage), "Pro features are now enabled.");

    public static string SettingsProPurchasePendingTitle =>
        GetString(nameof(SettingsProPurchasePendingTitle), "Checkout opened");

    public static string SettingsProPurchasePendingMessage =>
        GetString(nameof(SettingsProPurchasePendingMessage),
            "Complete the PayPal payment and enter your license key using Restore purchase.");

    public static string SettingsProPurchaseCancelledTitle =>
        GetString(nameof(SettingsProPurchaseCancelledTitle), "Purchase cancelled");

    public static string SettingsProPurchaseCancelledMessage =>
        GetString(nameof(SettingsProPurchaseCancelledMessage), "No changes were made.");

    public static string SettingsProPurchaseFailedTitle =>
        GetString(nameof(SettingsProPurchaseFailedTitle), "Purchase failed");

    public static string SettingsProPurchaseFailedMessage =>
        GetString(nameof(SettingsProPurchaseFailedMessage), "Please try again or restore your purchase later.");

    public static string SettingsProRestoreSuccessMessage =>
        GetString(nameof(SettingsProRestoreSuccessMessage), "Your Pro purchase has been restored.");

    public static string SettingsProRestoreFailedMessage =>
        GetString(nameof(SettingsProRestoreFailedMessage), "No Pro purchase was found for this account.");

    public static string SettingsProActivateTitle =>
        GetString(nameof(SettingsProActivateTitle), "Activate license");

    public static string SettingsProActivateMessage =>
        GetString(nameof(SettingsProActivateMessage), "Enter the license key you received after checkout.");

    public static string SettingsProActivateAccept =>
        GetString(nameof(SettingsProActivateAccept), "Activate");

    public static string SettingsProActivatePlaceholder =>
        GetString(nameof(SettingsProActivatePlaceholder), "License key");

    public static string SettingsProNotSupportedTitle =>
        GetString(nameof(SettingsProNotSupportedTitle), "Not supported");

    public static string SettingsProNotSupportedMessage =>
        GetString(nameof(SettingsProNotSupportedMessage), "Purchases are handled via the licensing server.");

    public static string HelpSectionTitle => GetString(nameof(HelpSectionTitle), "Help");
    public static string HelpSectionHint => GetString(nameof(HelpSectionHint), "About the app and license information.");
    public static string AboutTitle => GetString(nameof(AboutTitle), "About");
    public static string AboutDescription => GetString(nameof(AboutDescription), "Version details and license information.");
    public static string AboutLicenseStatusFormat => GetString(nameof(AboutLicenseStatusFormat), "License: {0}");
    public static string AboutVersionFormat => GetString(nameof(AboutVersionFormat), "Version {0}");
    public static string AboutLicensesButton => GetString(nameof(AboutLicensesButton), "License information");
    public static string LicenseInfoTitle => GetString(nameof(LicenseInfoTitle), "License information");
    public static string MarkedActionsMenuButton => GetString(nameof(MarkedActionsMenuButton), "Actions");

    public static string SettingsLocationsTitle =>
        GetString(nameof(SettingsLocationsTitle), "Locations");

    public static string SettingsLocationsHint =>
        GetString(nameof(SettingsLocationsHint),
            "Extract GPS metadata from photos and videos so they can be displayed on the world map. Map tiles are loaded from external tile servers.");

    public static string SettingsLocationsDisclaimer =>
        GetString(nameof(SettingsLocationsDisclaimer),
            "Location data stays on your device, but external map servers may receive request data (e.g. IP address). You are responsible for sharing sensitive locations; third-party services have their own terms and privacy policies.");

    public static string SettingsLocationsLabel =>
        GetString(nameof(SettingsLocationsLabel), "Show locations");

    public static string LocationOptInTitle =>
        GetString(nameof(LocationOptInTitle), "Enable location data?");

    public static string LocationOptInMessage =>
        GetString(nameof(LocationOptInMessage),
            "When enabled, the app reads GPS coordinates from EXIF metadata in your photos and videos. The map view loads tiles from external servers, which may receive request data (such as your IP address). Location data is stored locally on this device. By continuing, you accept that third-party services have their own terms and privacy policies.");

    public static string LocationOptInAccept => GetString(nameof(LocationOptInAccept), "Enable");
    public static string LocationOptInDecline => GetString(nameof(LocationOptInDecline), "Not now");

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

    public static string TagPeopleNoFacesAction =>
        GetString(nameof(TagPeopleNoFacesAction), "No people detected");

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

    public static string RemoveFromPeopleIndexButton =>
        GetString(nameof(RemoveFromPeopleIndexButton), "Remove from people index");

    public static string PersonPageTitle => GetString(nameof(PersonPageTitle), "Person");
    public static string PersonNamePlaceholder => GetString(nameof(PersonNamePlaceholder), "Name");
    public static string PersonEmptyHint => GetString(nameof(PersonEmptyHint), "No photos yet.");
    public static string IgnorePersonTitle => GetString(nameof(IgnorePersonTitle), "Ignore person");

    public static string IgnorePersonDescription =>
        GetString(nameof(IgnorePersonDescription), "Ignored people are hidden from automatic people tags.");

    public static string IgnorePersonAction => GetString(nameof(IgnorePersonAction), "Ignore");
    public static string UnignorePersonAction => GetString(nameof(UnignorePersonAction), "Stop ignoring");
    public static string IgnoredPersonBadge => GetString(nameof(IgnoredPersonBadge), "Ignored");

    public static string TagPeopleEditorTitle => GetString(nameof(TagPeopleEditorTitle), "Tag people");
    public static string TagPeopleEditorHeader => GetString(nameof(TagPeopleEditorHeader), "Name faces");

    public static string TagPeopleEditorTagsLabel => GetString(nameof(TagPeopleEditorTagsLabel), "People tags");

    public static string TagPeopleEditorTagsPlaceholder =>
        GetString(nameof(TagPeopleEditorTagsPlaceholder), "Add names, separated by commas...");

    public static string TagPeopleEditorTagsHint =>
        GetString(nameof(TagPeopleEditorTagsHint), "Tip: You can tag a photo even if no faces were detected.");

    public static string TagPeopleEditorEmptyHint =>
        GetString(nameof(TagPeopleEditorEmptyHint), "No faces detected for this photo.");

    public static string TagImageEditorTitle => GetString(nameof(TagImageEditorTitle), "Image tags");
    public static string TagImageEditorHeader => GetString(nameof(TagImageEditorHeader), "Image tags");
    public static string TagImageEditorTagsLabel => GetString(nameof(TagImageEditorTagsLabel), "Image tags");

    public static string TagImageEditorTagsPlaceholder =>
        GetString(nameof(TagImageEditorTagsPlaceholder), "Add tags, separated by commas...");

    public static string TagImageEditorTagsHint =>
        GetString(nameof(TagImageEditorTagsHint), "Tip: You can tag an image even if no automatic tags were detected.");

    public static string TagImageEditorEmptyHint =>
        GetString(nameof(TagImageEditorEmptyHint), "No image tags detected for this photo.");

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
    public static string MediaTypeGraphics => GetString(nameof(MediaTypeGraphics), "Graphics & screenshots");
    public static string MediaTypeFilterTitle => GetString(nameof(MediaTypeFilterTitle), "Media types");
    public static string SettingsMediaTypesTitle => GetString(nameof(SettingsMediaTypesTitle), "Media types");

    public static string SettingsMediaTypesMessage => GetString(nameof(SettingsMediaTypesMessage),
        "Choose which types to index in your library.");

    public static string SettingsExtensionsTitle => GetString(nameof(SettingsExtensionsTitle), "File extensions");

    public static string SettingsExtensionsHint => GetString(nameof(SettingsExtensionsHint),
        "Separate extensions with commas (e.g. .mp4, .mkv).");

    public static string SettingsExtensionsPlaceholder =>
        GetString(nameof(SettingsExtensionsPlaceholder), ".mp4, .mkv, .avi");

    public static string MapButton => GetString(nameof(MapButton), "Map");
    public static string MapPageTitle => GetString(nameof(MapPageTitle), "Map");
    public static string MapEmptyTitle => GetString(nameof(MapEmptyTitle), "No locations found");

    public static string MapEmptyMessage =>
        GetString(nameof(MapEmptyMessage), "Enable locations and reindex to see photos and videos on the map.");

    public static string OpenAction => GetString(nameof(OpenAction), "Open");

    public static string RenameConfirmTitle => GetString(nameof(RenameConfirmTitle), "Confirm rename");

    public static string RenameConfirmMessage =>
        GetString(nameof(RenameConfirmMessage), "Rename \"{0}\" to \"{1}\"?");

    public static string LegalImprintTitle => GetString(nameof(LegalImprintTitle), "Imprint");
    public static string LegalPrivacyTitle => GetString(nameof(LegalPrivacyTitle), "Privacy policy");
    public static string LegalTermsTitle => GetString(nameof(LegalTermsTitle), "Terms & conditions");
    public static string LegalWithdrawalTitle => GetString(nameof(LegalWithdrawalTitle), "Withdrawal");
    public static string LegalPrivacyBody => GetString(nameof(LegalPrivacyBody), string.Empty);
    public static string LegalTermsBody => GetString(nameof(LegalTermsBody), string.Empty);
    public static string LegalWithdrawalBody => GetString(nameof(LegalWithdrawalBody), string.Empty);
    public static string LegalConsentTitle => GetString(nameof(LegalConsentTitle), "Legal consent");
    public static string LegalConsentHeader => GetString(nameof(LegalConsentHeader), "Legal consent");
    public static string LegalConsentIntro => GetString(nameof(LegalConsentIntro), string.Empty);
    public static string LegalConsentProductTitle => GetString(nameof(LegalConsentProductTitle), string.Empty);
    public static string LegalConsentProductScope => GetString(nameof(LegalConsentProductScope), string.Empty);
    public static string LegalConsentPriceFormat => GetString(nameof(LegalConsentPriceFormat), "{0}");
    public static string LegalConsentPaymentHint => GetString(nameof(LegalConsentPaymentHint), string.Empty);
    public static string LegalConsentWithdrawalTitle => GetString(nameof(LegalConsentWithdrawalTitle), string.Empty);
    public static string LegalConsentWithdrawalCheckbox => GetString(nameof(LegalConsentWithdrawalCheckbox), string.Empty);
    public static string LegalConsentWithdrawalHint => GetString(nameof(LegalConsentWithdrawalHint), string.Empty);
    public static string LegalConsentDocumentsTitle => GetString(nameof(LegalConsentDocumentsTitle), string.Empty);
    public static string LegalConsentDocumentsHint => GetString(nameof(LegalConsentDocumentsHint), string.Empty);
    public static string LegalConsentConfirmButton => GetString(nameof(LegalConsentConfirmButton), "Confirm");
    public static string LegalConsentMissingTitle => GetString(nameof(LegalConsentMissingTitle), "Consent required");
    public static string LegalConsentMissingMessage =>
        GetString(nameof(LegalConsentMissingMessage), "Please confirm before continuing.");

    private static string GetString(string key, string fallback)
    {
        return ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? fallback;
    }
}
