namespace UltimateVideoBrowser.Services;

public interface IBackupRestoreService
{
    Task ExportBackupAsync(CancellationToken ct);
    Task ImportBackupAsync(CancellationToken ct);
}