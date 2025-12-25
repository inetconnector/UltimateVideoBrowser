using Android.App;
using Android.Content;
using AndroidX.DocumentFile.Provider;
using UltimateVideoBrowser.Services;

namespace UltimateVideoBrowser.Platforms.Android;

public sealed class FolderPickerService : IFolderPickerService
{
    const int RequestCode = 9021;
    static TaskCompletionSource<FolderPickResult?>? pending;

    public Task<FolderPickResult?> PickFolderAsync(CancellationToken ct = default)
    {
        if (pending != null)
            return pending.Task;

        var activity = Platform.CurrentActivity;
        if (activity == null)
            return Task.FromResult<FolderPickResult?>(null);

        pending = new TaskCompletionSource<FolderPickResult?>();
        ct.Register(() =>
        {
            pending?.TrySetCanceled();
            pending = null;
        });

        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission |
                        ActivityFlags.GrantPersistableUriPermission |
                        ActivityFlags.GrantPrefixUriPermission);

        activity.StartActivityForResult(intent, RequestCode);
        return pending.Task;
    }

    internal static void HandleActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode != RequestCode)
            return;

        var tcs = pending;
        pending = null;

        if (tcs == null)
            return;

        if (resultCode != Result.Ok || data?.Data == null)
        {
            tcs.TrySetResult(null);
            return;
        }

        var uri = data.Data;
        var activity = Platform.CurrentActivity;
        if (activity != null)
        {
            var flags = data.Flags & (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
            try
            {
                activity.ContentResolver?.TakePersistableUriPermission(uri, flags);
            }
            catch
            {
                // Ignore failures, best-effort persistable permission.
            }
        }

        var name = activity == null ? uri.LastPathSegment ?? "Folder" : DocumentFile.FromTreeUri(activity, uri)?.Name ?? uri.LastPathSegment ?? "Folder";
        tcs.TrySetResult(new FolderPickResult(uri.ToString(), name));
    }
}
