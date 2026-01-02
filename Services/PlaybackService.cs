using UltimateVideoBrowser.Models;

#if ANDROID && !WINDOWS
using Android.Content;
using Uri = Android.Net.Uri;

#elif WINDOWS
#endif

namespace UltimateVideoBrowser.Services;

public sealed class PlaybackService
{
    public void Open(MediaItem item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Path))
            return;

#if ANDROID && !WINDOWS
        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(Uri.Parse(item.Path), GetAndroidMimeType(item.MediaType));
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);
        Platform.AppContext.StartActivity(intent);
#elif WINDOWS
        _ = Launcher.OpenAsync(new OpenFileRequest("Open file", new ReadOnlyFile(item.Path)));
#else
        _ = item;
#endif
    }

#if ANDROID && !WINDOWS
    private static string GetAndroidMimeType(MediaType mediaType)
    {
        return mediaType switch
        {
            MediaType.Photos => "image/*",
            MediaType.Graphics => "image/*",
            MediaType.Documents => "*/*",
            _ => "video/*"
        };
    }
#endif
}
