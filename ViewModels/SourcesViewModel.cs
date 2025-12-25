using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Models;
using UltimateVideoBrowser.Services;

namespace UltimateVideoBrowser.ViewModels;

public partial class SourcesViewModel : ObservableObject
{
    readonly SourceService sourceService;

    [ObservableProperty] List<MediaSource> sources = new();

    public SourcesViewModel(SourceService sourceService)
    {
        this.sourceService = sourceService;
    }

    public async Task LoadAsync()
    {
        Sources = await sourceService.GetSourcesAsync();
    }

    [RelayCommand]
    public async Task ToggleAsync(MediaSource src)
    {
        src.IsEnabled = !src.IsEnabled;
        await sourceService.UpsertAsync(src);
        await LoadAsync();
    }
}
