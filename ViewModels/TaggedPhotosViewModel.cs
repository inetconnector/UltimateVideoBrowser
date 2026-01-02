using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Services;

namespace UltimateVideoBrowser.ViewModels;

public sealed partial class TaggedPhotosViewModel : ObservableObject
{
    private readonly AppDb db;
    private readonly PeopleTagService peopleTags;
    private readonly ThumbnailService thumbnails;

    private CancellationTokenSource? debounceCts;

    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private ObservableCollection<MediaItem> photos = new();
    [ObservableProperty] private string searchText = string.Empty;

    public TaggedPhotosViewModel(AppDb db, PeopleTagService peopleTags, ThumbnailService thumbnails)
    {
        this.db = db;
        this.peopleTags = peopleTags;
        this.thumbnails = thumbnails;
    }

    partial void OnSearchTextChanged(string value)
    {
        debounceCts?.Cancel();
        debounceCts = new CancellationTokenSource();
        var token = debounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, token).ConfigureAwait(false);
                await RefreshAsync(token).ConfigureAwait(false);
            }
            catch
            {
                // Ignore
            }
        }, token);
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await RefreshAsync(CancellationToken.None).ConfigureAwait(false);
    }


    [RelayCommand]
    public async Task RemoveFromPeopleIndexAsync(MediaItem? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Path))
            return;

        try
        {
            await db.EnsureInitializedAsync().ConfigureAwait(false);

            // Remove manual tags and automatic face embeddings for this media item.
            await db.Db.ExecuteAsync("DELETE FROM PersonTag WHERE MediaPath = ?;", item.Path).ConfigureAwait(false);
            await db.Db.ExecuteAsync("DELETE FROM FaceEmbedding WHERE MediaPath = ?;", item.Path).ConfigureAwait(false);

            // Update current list immediately.
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                try
                {
                    Photos.Remove(item);
                }
                catch
                {
                    // Ignore collection issues.
                }
            }).ConfigureAwait(false);
        }
        catch
        {
            // Keep UI resilient.
        }
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            IsBusy = true;
            await db.EnsureInitializedAsync().ConfigureAwait(false);

            var term = (SearchText ?? string.Empty).Trim();

            // PersonTag is the authoritative store for manual tags, but for resilience we also
            // fall back to MediaItem.PeopleTagsSummary (e.g. if tags were written there but the
            // PersonTag table is temporarily out of sync).
            var fallbackPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!string.IsNullOrWhiteSpace(term))
                {
                    var fall = await db.Db.QueryAsync<PathRow>(
                            "SELECT DISTINCT Path AS MediaPath FROM MediaItem WHERE MediaType = ? AND PeopleTagsSummary LIKE ?;",
                            (int)MediaType.Photos,
                            $"%{term}%")
                        .ConfigureAwait(false);

                    foreach (var r in fall)
                        if (!string.IsNullOrWhiteSpace(r.MediaPath))
                            fallbackPaths.Add(r.MediaPath);
                }
            }
            catch
            {
                // Keep UI resilient.
            }

            List<PathRow> rows;
            if (string.IsNullOrWhiteSpace(term))
                rows = await db.Db.QueryAsync<PathRow>(
                        "SELECT DISTINCT MediaPath FROM PersonTag ORDER BY MediaPath DESC;")
                    .ConfigureAwait(false);
            else
                rows = await db.Db.QueryAsync<PathRow>(
                        "SELECT DISTINCT MediaPath FROM PersonTag WHERE PersonName LIKE ? ORDER BY MediaPath DESC;",
                        $"%{term}%")
                    .ConfigureAwait(false);

            var paths = rows
                .Select(r => r.MediaPath)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var fp in fallbackPaths)
                paths.Add(fp);

            paths = paths
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (paths.Count == 0)
            {
                await MainThread.InvokeOnMainThreadAsync(() => Photos = new ObservableCollection<MediaItem>());
                return;
            }

            // Query MediaItem rows in chunks to keep SQLite parameter limits safe.
            var items = new List<MediaItem>();
            const int chunkSize = 200;
            for (var i = 0; i < paths.Count; i += chunkSize)
            {
                ct.ThrowIfCancellationRequested();

                var chunk = paths.Skip(i).Take(chunkSize).ToList();
                var placeholders = string.Join(", ", chunk.Select(_ => "?"));
                var sql =
                    $"SELECT * FROM MediaItem WHERE Path IN ({placeholders}) AND MediaType = ? ORDER BY DateAddedSeconds DESC;";

                var args = chunk.Cast<object>().ToList();
                args.Add((int)MediaType.Photos);

                var result = await db.Db.QueryAsync<MediaItem>(sql, args.ToArray()).ConfigureAwait(false);
                items.AddRange(result);
            }

            var tagsByPath = await peopleTags.GetTagsForMediaAsync(items.Select(x => x.Path)).ConfigureAwait(false);
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                if (tagsByPath.TryGetValue(item.Path, out var tags) && tags.Count > 0)
                    item.PeopleTagsSummary = string.Join(", ", tags.Distinct(StringComparer.OrdinalIgnoreCase));
                else
                    item.PeopleTagsSummary = string.Empty;
            }

            await MainThread.InvokeOnMainThreadAsync(() => { Photos = new ObservableCollection<MediaItem>(items); });

            foreach (var item in items)
                _ = EnsureAndApplyThumbnailAsync(item);
        }
        catch
        {
            // Ignore
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsBusy = false);
        }
    }

    private async Task EnsureAndApplyThumbnailAsync(MediaItem item)
    {
        // Ensure that the UI shows a placeholder while we (re)generate the thumbnail.
        MainThread.BeginInvokeOnMainThread(() => item.ThumbnailPath = string.Empty);

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var p = await thumbnails
                .EnsureThumbnailWithRetryAsync(item, TimeSpan.FromMinutes(2), TimeSpan.FromSeconds(2), cts.Token)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(p))
                return;

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (string.Equals(item.ThumbnailPath, p, StringComparison.OrdinalIgnoreCase))
                    item.ThumbnailPath = string.Empty;

                item.ThumbnailPath = p;
            });
        }
        catch
        {
            // Ignore
        }
    }

    private sealed class PathRow
    {
        public string MediaPath { get; } = string.Empty;
    }
}
