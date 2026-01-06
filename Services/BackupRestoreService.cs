using System.IO.Compression;
using UltimateVideoBrowser.Resources.Strings;

#if WINDOWS
using Windows.Storage.Pickers;
using WinRT.Interop;
#endif

namespace UltimateVideoBrowser.Services;

public sealed class BackupRestoreService : IBackupRestoreService
{
    private const string BackupDbFileName = "ultimatevideobrowser.db";
    private const string BackupSettingsFileName = "settings.json";
    private const string BackupThumbsFolderName = "thumbs";

    private readonly AppDb db;
    private readonly AppSettingsService settingsService;
    private readonly FileSettingsStore settingsStore;
    private readonly ThumbnailService thumbnailService;
    private readonly IDialogService dialogService;

    public BackupRestoreService(
        AppDb db,
        AppSettingsService settingsService,
        FileSettingsStore settingsStore,
        ThumbnailService thumbnailService,
        IDialogService dialogService)
    {
        this.db = db;
        this.settingsService = settingsService;
        this.settingsStore = settingsStore;
        this.thumbnailService = thumbnailService;
        this.dialogService = dialogService;
    }

    public async Task ExportBackupAsync(CancellationToken ct)
    {
        try
        {
            await db.EnsureInitializedAsync().ConfigureAwait(false);

            var tempPath = Path.Combine(FileSystem.CacheDirectory,
                $"uvb-backup-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.zip");

            if (File.Exists(tempPath))
                File.Delete(tempPath);

            CreateBackupZip(tempPath);

#if WINDOWS
            var destination = await PickSaveLocationAsync().ConfigureAwait(false);
            if (destination is null)
                return;

            await using (var sourceStream = File.OpenRead(tempPath))
            await using (var destinationStream = await destination.OpenStreamForWriteAsync())
            {
                destinationStream.SetLength(0);
                await sourceStream.CopyToAsync(destinationStream, ct).ConfigureAwait(false);
            }

            await dialogService.DisplayAlertAsync(
                AppResources.BackupExportSuccessTitle,
                AppResources.BackupExportSuccessMessage,
                AppResources.OkButton).ConfigureAwait(false);
#else
            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = AppResources.BackupExportButton,
                File = new ShareFile(tempPath)
            });
#endif
        }
        catch
        {
            await dialogService.DisplayAlertAsync(
                AppResources.BackupExportFailedTitle,
                AppResources.BackupExportFailedMessage,
                AppResources.OkButton).ConfigureAwait(false);
        }
    }

    public async Task ImportBackupAsync(CancellationToken ct)
    {
        if (settingsService.IsIndexing)
        {
            await dialogService.DisplayAlertAsync(
                AppResources.BackupImportNotAllowedTitle,
                AppResources.BackupImportNotAllowedMessage,
                AppResources.OkButton).ConfigureAwait(false);
            return;
        }

        try
        {
            var pickedPath = await PickBackupZipAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(pickedPath))
                return;

            var confirm = await dialogService.DisplayAlertAsync(
                AppResources.BackupImportConfirmTitle,
                AppResources.BackupImportConfirmMessage,
                AppResources.RestoreButton,
                AppResources.CancelButton).ConfigureAwait(false);

            if (!confirm)
                return;

            var extractRoot = Path.Combine(FileSystem.CacheDirectory, "uvb-restore");
            if (Directory.Exists(extractRoot))
                Directory.Delete(extractRoot, true);
            Directory.CreateDirectory(extractRoot);

            ZipFile.ExtractToDirectory(pickedPath, extractRoot, true);

            var extractedDb = Path.Combine(extractRoot, BackupDbFileName);
            if (!File.Exists(extractedDb))
            {
                await dialogService.DisplayAlertAsync(
                    AppResources.BackupImportFailedTitle,
                    AppResources.BackupImportMissingDbMessage,
                    AppResources.OkButton).ConfigureAwait(false);
                return;
            }

            var extractedSettings = Path.Combine(extractRoot, BackupSettingsFileName);
            var extractedThumbs = Path.Combine(extractRoot, BackupThumbsFolderName);

            await db.ReplaceDatabaseAsync(extractedDb).ConfigureAwait(false);

            if (File.Exists(extractedSettings))
                settingsStore.ReplaceFromFile(extractedSettings);

            if (Directory.Exists(extractedThumbs))
            {
                var targetThumbs = thumbnailService.ThumbnailsDirectoryPath;
                TryDeleteDirectory(targetThumbs);
                CopyDirectory(extractedThumbs, targetThumbs);
            }

            settingsService.ReloadFromDisk();

            await dialogService.DisplayAlertAsync(
                AppResources.BackupImportSuccessTitle,
                AppResources.BackupImportSuccessMessage,
                AppResources.OkButton).ConfigureAwait(false);
        }
        catch
        {
            await dialogService.DisplayAlertAsync(
                AppResources.BackupImportFailedTitle,
                AppResources.BackupImportFailedMessage,
                AppResources.OkButton).ConfigureAwait(false);
        }
    }

    private void CreateBackupZip(string targetZipPath)
    {
        using var zip = ZipFile.Open(targetZipPath, ZipArchiveMode.Create);

        // DB
        AddFileIfExists(zip, db.DatabasePath, BackupDbFileName);
        AddFileIfExists(zip, db.DatabasePath + "-wal", BackupDbFileName + "-wal");
        AddFileIfExists(zip, db.DatabasePath + "-shm", BackupDbFileName + "-shm");

        // Settings
        AddFileIfExists(zip, settingsStore.SettingsPath, BackupSettingsFileName);

        // Thumbnails
        var thumbsDir = thumbnailService.ThumbnailsDirectoryPath;
        if (Directory.Exists(thumbsDir))
            AddDirectory(zip, thumbsDir, BackupThumbsFolderName);
    }

    private static void AddFileIfExists(ZipArchive zip, string sourcePath, string entryName)
    {
        if (!File.Exists(sourcePath))
            return;

        zip.CreateEntryFromFile(sourcePath, entryName, CompressionLevel.Optimal);
    }

    private static void AddDirectory(ZipArchive zip, string sourceDir, string entryRoot)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var entryName = Path.Combine(entryRoot, relative).Replace('\\', '/');
            zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
        }
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var destination = Path.Combine(targetDir, relative);
            var directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
            File.Copy(file, destination, true);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
        }
    }

    private static string? GetPickedFilePath(FileResult picked)
    {
        // Some platforms provide a temporary local path, others only a stream.
        // If we can't access the path, persist to a local temp file.
        if (!string.IsNullOrWhiteSpace(picked.FullPath) && File.Exists(picked.FullPath))
            return picked.FullPath;

        return null;
    }

    private static async Task<string?> PersistPickedFileToCacheAsync(FileResult picked, CancellationToken ct)
    {
        var cachePath = Path.Combine(FileSystem.CacheDirectory,
            $"uvb-import-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.zip");

        await using var inStream = await picked.OpenReadAsync();
        await using var outStream = File.Create(cachePath);
        await inStream.CopyToAsync(outStream, ct).ConfigureAwait(false);
        return cachePath;
    }

    private static async Task<string?> PickBackupZipAsync(CancellationToken ct)
    {
#if WINDOWS
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".zip");

            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Handler?.PlatformView is not MauiWinUIWindow mauiWindow)
                return null;

            InitializeWithWindow.Initialize(picker, mauiWindow.WindowHandle);
            var file = await picker.PickSingleFileAsync();
            return file?.Path;
        }
        catch
        {
            return null;
        }
#else
        try
        {
            var picked = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = AppResources.BackupImportPickerTitle,
                FileTypes = GetZipFileTypes()
            }).ConfigureAwait(false);

            if (picked == null)
                return null;

            var path = GetPickedFilePath(picked);
            if (path != null)
                return path;

            return await PersistPickedFileToCacheAsync(picked, ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
#endif
    }

#if !WINDOWS
    private static FilePickerFileType GetZipFileTypes()
    {
        return new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.Android, new[] { "application/zip", "application/x-zip", "application/x-zip-compressed" } },
            { DevicePlatform.WinUI, new[] { ".zip" } },
            { DevicePlatform.MacCatalyst, new[] { "public.zip-archive" } },
            { DevicePlatform.iOS, new[] { "public.zip-archive" } }
        });
    }
#endif

#if WINDOWS
    private static async Task<Windows.Storage.StorageFile?> PickSaveLocationAsync()
    {
        try
        {
            var picker = new FileSavePicker();
            picker.FileTypeChoices.Add("Backup", new List<string> { ".zip" });
            picker.SuggestedFileName = $"uvb-backup-{DateTimeOffset.Now:yyyyMMdd-HHmmss}";

            var window = Application.Current?.Windows.FirstOrDefault();
            if (window?.Handler?.PlatformView is not MauiWinUIWindow mauiWindow)
                return null;

            InitializeWithWindow.Initialize(picker, mauiWindow.WindowHandle);
            return await picker.PickSaveFileAsync();
        }
        catch
        {
            return null;
        }
    }
#endif
}
