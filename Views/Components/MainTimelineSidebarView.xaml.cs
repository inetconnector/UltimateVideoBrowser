namespace UltimateVideoBrowser.Views.Components;

public partial class MainTimelineSidebarView : ContentView
{
    public MainTimelineSidebarView()
    {
        InitializeComponent();
    }

    public CollectionView TimelineView => TimelineCollection;

    public event EventHandler? MediaScrollUpClicked;
    public event EventHandler? MediaScrollDownClicked;
    public event EventHandler? MediaScrollTopClicked;
    public event EventHandler? MediaScrollBottomClicked;
    public event EventHandler? TimelineScrollUpClicked;
    public event EventHandler? TimelineScrollDownClicked;
    public event EventHandler? TimelineScrollTopClicked;
    public event EventHandler? TimelineScrollBottomClicked;
    public event EventHandler? SettingsClicked;
    public event EventHandler<SelectionChangedEventArgs>? TimelineSelectionChanged;

    private void OnMediaScrollUpClicked(object sender, EventArgs e)
    {
        MediaScrollUpClicked?.Invoke(this, e);
    }

    private void OnMediaScrollDownClicked(object sender, EventArgs e)
    {
        MediaScrollDownClicked?.Invoke(this, e);
    }

    private void OnMediaScrollTopClicked(object sender, EventArgs e)
    {
        MediaScrollTopClicked?.Invoke(this, e);
    }

    private void OnMediaScrollBottomClicked(object sender, EventArgs e)
    {
        MediaScrollBottomClicked?.Invoke(this, e);
    }

    private void OnTimelineScrollUpClicked(object sender, EventArgs e)
    {
        TimelineScrollUpClicked?.Invoke(this, e);
    }

    private void OnTimelineScrollDownClicked(object sender, EventArgs e)
    {
        TimelineScrollDownClicked?.Invoke(this, e);
    }

    private void OnTimelineScrollTopClicked(object sender, EventArgs e)
    {
        TimelineScrollTopClicked?.Invoke(this, e);
    }

    private void OnTimelineScrollBottomClicked(object sender, EventArgs e)
    {
        TimelineScrollBottomClicked?.Invoke(this, e);
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
