using UltimateVideoBrowser.Models;

#if ANDROID
using Android.Content;
#endif

namespace UltimateVideoBrowser.Services;

public sealed class PlaybackService
{
    public void Play(VideoItem item)
    {
#if ANDROID
        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(Android.Net.Uri.Parse(item.Path), "video/*");
        intent.AddFlags(ActivityFlags.NewTask);
        Android.App.Application.Context.StartActivity(intent);
#else
        _ = item;
#endif
    }
}
