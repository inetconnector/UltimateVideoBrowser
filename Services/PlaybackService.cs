using Android.Content;
using UltimateVideoBrowser.Models;

namespace UltimateVideoBrowser.Services;

public sealed class PlaybackService
{
    public void Play(VideoItem item)
    {
        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(Android.Net.Uri.Parse(item.Path), "video/*");
        intent.AddFlags(ActivityFlags.NewTask);
        Android.App.Application.Context.StartActivity(intent);
    }
}
