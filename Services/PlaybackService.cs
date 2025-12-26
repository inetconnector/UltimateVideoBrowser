using UltimateVideoBrowser.Models;
#if ANDROID && !WINDOWS
using Android.Content;
using Uri = Android.Net.Uri;
#elif WINDOWS
#endif

namespace UltimateVideoBrowser.Services;

public sealed class PlaybackService
{
    public void Play(VideoItem item)
    {
#if ANDROID && !WINDOWS
        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(Uri.Parse(item.Path), "video/*");
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);
        Platform.AppContext.StartActivity(intent);
#elif WINDOWS
        _ = Launcher.OpenAsync(new OpenFileRequest("Play video", new ReadOnlyFile(item.Path)));
#else
        _ = item;
#endif
    }
}