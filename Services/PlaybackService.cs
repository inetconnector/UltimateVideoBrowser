using UltimateVideoBrowser.Models;

#if ANDROID
using Android.Content;
#elif WINDOWS
using Microsoft.Maui.Storage;
#endif

namespace UltimateVideoBrowser.Services;

public sealed class PlaybackService
{
    public void Play(VideoItem item)
    {
#if ANDROID
        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(Android.Net.Uri.Parse(item.Path), "video/*");
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);
        Android.App.Application.Context.StartActivity(intent);
#elif WINDOWS
        _ = Launcher.OpenAsync(new OpenFileRequest("Play video", new ReadOnlyFile(item.Path)));
#else
        _ = item;
#endif
    }
}
