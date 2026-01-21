using Android.App;
using Android.Content;
using AndroidX.DocumentFile.Provider;
using UltimateVideoBrowser.Services;

namespace UltimateVideoBrowser.Platforms.Android;

public sealed class FolderPickerService : IFolderPickerService
{
    private const int RequestCode = 9021;
    private static TaskCompletionSource<IReadOnlyList<FolderPickResult>>? pending;

    public Task<IReadOnlyList<FolderPickResult>> PickFoldersAsync(CancellationToken ct = default)
    {
        if (pending != null)
            return pending.Task;

        var activity = Platform.CurrentActivity;
        if (activity == null)
            return Task.FromResult<IReadOnlyList<FolderPickResult>>(Array.Empty<FolderPickResult>());

        pending = new TaskCompletionSource<IReadOnlyList<FolderPickResult>>();
        ct.Register(() =>
        {
            pending?.TrySetCanceled();
            pending = null;
        });

        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.PutExtra(Intent.ExtraAllowMultiple, true);
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

        if (resultCode != Result.Ok || data == null)
        {
            tcs.TrySetResult(Array.Empty<FolderPickResult>());
            return;
        }

        var activity = Platform.CurrentActivity;
        var flags = data.Flags &
                    (ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
        var results = new List<FolderPickResult>();
        if (data.ClipData != null)
        {
            for (var i = 0; i < data.ClipData.ItemCount; i++)
            {
                var uri = data.ClipData.GetItemAt(i)?.Uri;
                if (uri != null)
                    AddPickResult(results, activity, uri, flags);
            }
        }
        else if (data.Data != null)
        {
            AddPickResult(results, activity, data.Data, flags);
        }

        tcs.TrySetResult(results);
    }

    private static void AddPickResult(
        ICollection<FolderPickResult> results,
        Activity? activity,
        Android.Net.Uri uri,
        ActivityFlags flags)
    {
        if (activity != null)
        {
            try
            {
                activity.ContentResolver?.TakePersistableUriPermission(uri, flags);
            }
            catch
            {
                // Ignore failures, best-effort persistable permission.
            }
        }

        var name = activity == null
            ? uri.LastPathSegment ?? "Folder"
            : DocumentFile.FromTreeUri(activity, uri)?.Name ?? uri.LastPathSegment ?? "Folder";
        results.Add(new FolderPickResult(uri.ToString() ?? string.Empty, name));
    }
}
