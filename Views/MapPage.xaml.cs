using System.Net;
using UltimateVideoBrowser.ViewModels;

namespace UltimateVideoBrowser.Views;

public partial class MapPage : ContentPage
{
    private readonly MapViewModel vm;

    public MapPage(MapViewModel vm)
    {
        InitializeComponent();
        this.vm = vm;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await vm.LoadAsync();
        MapWebView.Source = new HtmlWebViewSource
        {
            Html = vm.MapHtml
        };
    }

    private void OnMapNavigating(object sender, WebNavigatingEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Url) ||
            !e.Url.StartsWith("uvb://media", StringComparison.OrdinalIgnoreCase))
            return;

        e.Cancel = true;
        var uri = new Uri(e.Url);
        var path = GetQueryValue(uri, "path");
        if (!string.IsNullOrWhiteSpace(path))
            vm.TrySelectByPath(path);
    }

    private static string? GetQueryValue(Uri uri, string key)
    {
        var query = uri.Query;
        if (string.IsNullOrWhiteSpace(query))
            return null;

        var trimmed = query.TrimStart('?');
        var parts = trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2)
                continue;

            var name = WebUtility.UrlDecode(kv[0] ?? string.Empty);
            if (!string.Equals(name, key, StringComparison.OrdinalIgnoreCase))
                continue;

            return WebUtility.UrlDecode(kv[1] ?? string.Empty);
        }

        return null;
    }
}
