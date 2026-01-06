using System.Collections.ObjectModel;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Resources.Strings;
using UltimateVideoBrowser.Services;

namespace UltimateVideoBrowser.ViewModels;

public partial class MapViewModel : ObservableObject
{
    private readonly IFileExportService fileExportService;
    private readonly IndexService indexService;
    private readonly Dictionary<string, MediaItem> itemLookup = new(StringComparer.OrdinalIgnoreCase);
    private readonly PlaybackService playbackService;
    private readonly AppSettingsService settingsService;

    [ObservableProperty] private bool hasLocations;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private MediaItem? selectedItem;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private string statusTitle = string.Empty;

    public MapViewModel(
        IndexService indexService,
        AppSettingsService settingsService,
        PlaybackService playbackService,
        IFileExportService fileExportService)
    {
        this.indexService = indexService;
        this.settingsService = settingsService;
        this.playbackService = playbackService;
        this.fileExportService = fileExportService;
    }

    public ObservableCollection<MediaItem> Items { get; } = new();

    public string MapHtml { get; private set; } = string.Empty;

    public bool HasSelection => SelectedItem != null;

    public async Task LoadAsync()
    {
        if (IsLoading)
            return;

        IsLoading = true;
        try
        {
            Items.Clear();
            itemLookup.Clear();
            SelectedItem = null;

            var mediaTypes = settingsService.VisibleMediaTypes & (MediaType.Photos | MediaType.Videos);
            if (mediaTypes == MediaType.None)
                mediaTypes = MediaType.Photos | MediaType.Videos;

            var items = await indexService.QueryLocationsAsync(mediaTypes).ConfigureAwait(false);
            foreach (var item in items)
            {
                Items.Add(item);
                if (!string.IsNullOrWhiteSpace(item.Path))
                    itemLookup[item.Path] = item;
            }

            HasLocations = Items.Count > 0;
            StatusTitle = HasLocations ? string.Empty : AppResources.MapEmptyTitle;
            StatusMessage = HasLocations ? string.Empty : AppResources.MapEmptyMessage;
            MapHtml = BuildMapHtml(Items);
        }
        catch
        {
            HasLocations = false;
            StatusTitle = AppResources.MapEmptyTitle;
            StatusMessage = AppResources.MapEmptyMessage;
            MapHtml = BuildMapHtml(Array.Empty<MediaItem>());
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(MapHtml));
        }
    }

    public bool TrySelectByPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (!itemLookup.TryGetValue(path, out var item))
            return false;

        SelectedItem = item;
        return true;
    }

    partial void OnSelectedItemChanged(MediaItem? value)
    {
        OnPropertyChanged(nameof(HasSelection));
    }

    [RelayCommand]
    private void OpenSelected()
    {
        if (SelectedItem == null)
            return;

        playbackService.Open(SelectedItem);
    }

    [RelayCommand]
    private async Task ShareSelectedAsync()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.Path))
            return;

        await Share.Default.RequestAsync(new ShareFileRequest
        {
            Title = AppResources.ShareTitle,
            File = new ShareFile(SelectedItem.Path)
        });
    }

    [RelayCommand]
    private Task SaveAsSelectedAsync()
    {
        if (SelectedItem == null || string.IsNullOrWhiteSpace(SelectedItem.Path))
            return Task.CompletedTask;

        return fileExportService.SaveAsAsync(SelectedItem);
    }

    private static string BuildMapHtml(IReadOnlyCollection<MediaItem> items)
    {
        var mapItems = items
            .Where(item => item.Latitude.HasValue && item.Longitude.HasValue)
            .Select(item => new MapItem(item.Path ?? string.Empty, item.Name ?? string.Empty,
                item.Latitude!.Value, item.Longitude!.Value))
            .ToList();

        var json = JsonSerializer.Serialize(mapItems, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        var openLabel = JsonSerializer.Serialize(AppResources.OpenAction, new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta name=\"viewport\" content=\"initial-scale=1, width=device-width\">");
        sb.AppendLine("<link rel=\"stylesheet\" href=\"https://unpkg.com/leaflet@1.9.4/dist/leaflet.css\">");
        sb.AppendLine("<style>");
        sb.AppendLine("html, body, #map { height: 100%; margin: 0; background: #0f172a; }");
        sb.AppendLine(".leaflet-popup-content { font-family: -apple-system, Segoe UI, sans-serif; }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div id=\"map\"></div>");
        sb.AppendLine("<script src=\"https://unpkg.com/leaflet@1.9.4/dist/leaflet.js\"></script>");
        sb.AppendLine("<script>");
        sb.AppendLine("const items = " + json + ";");
        sb.AppendLine("const openLabel = " + openLabel + ";");
        sb.AppendLine("const map = L.map('map', { zoomControl: true }).setView([20, 0], 2);");
        sb.AppendLine(
            "L.tileLayer('https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png', { maxZoom: 19, attribution: '© OpenStreetMap contributors' }).addTo(map);");
        sb.AppendLine(
            "function escapeHtml(str) { return str.replace(/[&<>\"']/g, c => ({\"&\":\"&amp;\",\"<\":\"&lt;\",\">\":\"&gt;\",\"\\\"\":\"&quot;\",\"'\":\"&#39;\"}[c])); }");
        sb.AppendLine("const markers = [];");
        sb.AppendLine("items.forEach(item => {");
        sb.AppendLine("  const marker = L.marker([item.lat, item.lon]).addTo(map);");
        sb.AppendLine(
            "  const label = escapeHtml(item.name || '');");
        sb.AppendLine(
            "  marker.bindPopup(`<strong>${label}</strong><br/><a href=\"uvb://media?path=${encodeURIComponent(item.path)}\">${openLabel}</a>`);");
        sb.AppendLine("  markers.push(marker);");
        sb.AppendLine("});");
        sb.AppendLine("if (markers.length > 0) {");
        sb.AppendLine("  const group = L.featureGroup(markers);");
        sb.AppendLine("  map.fitBounds(group.getBounds().pad(0.2));");
        sb.AppendLine("}");
        sb.AppendLine("</script>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }

    private sealed record MapItem(string Path, string Name, double Lat, double Lon);
}