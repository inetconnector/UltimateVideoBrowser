namespace UltimateVideoBrowser.Views.Components;

public partial class MainTimelineSidebarView : ContentView
{
    public MainTimelineSidebarView()
    {
        InitializeComponent();
    }

    public event EventHandler? ScrollUpClicked;
    public event EventHandler? ScrollDownClicked;
    public event EventHandler<SelectionChangedEventArgs>? TimelineSelectionChanged;

    public CollectionView TimelineView => TimelineCollection;

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
}
