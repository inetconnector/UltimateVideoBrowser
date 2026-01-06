namespace UltimateVideoBrowser.Views.Components;

public partial class MainTimelineSidebarView : ContentView
{
    public MainTimelineSidebarView()
    {
        InitializeComponent();
    }

    public CollectionView TimelineView => TimelineCollection;

    public event EventHandler? ScrollUpClicked;
    public event EventHandler? ScrollDownClicked;
    public event EventHandler? SettingsClicked;
    public event EventHandler<SelectionChangedEventArgs>? TimelineSelectionChanged;

    private void OnScrollUpClicked(object sender, EventArgs e)
    {
        ScrollUpClicked?.Invoke(this, e);
    }

    private void OnScrollDownClicked(object sender, EventArgs e)
    {
        ScrollDownClicked?.Invoke(this, e);
    }

    private void OnTimelineSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        TimelineSelectionChanged?.Invoke(this, e);
    }

    private void OnSettingsClicked(object sender, EventArgs e)
    {
        SettingsClicked?.Invoke(this, e);
    }
}