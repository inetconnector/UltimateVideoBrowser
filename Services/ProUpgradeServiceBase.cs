namespace UltimateVideoBrowser.Services;

public abstract class ProUpgradeServiceBase : IProUpgradeService
{
    protected ProUpgradeServiceBase(AppSettingsService settingsService)
    {
        SettingsService = settingsService;
    }

    protected AppSettingsService SettingsService { get; }
    public abstract string ProductId { get; }

    public bool IsProUnlocked => SettingsService.IsProUnlocked;
    public bool HasReachedFreeLimit { get; private set; }
    public string? PriceText { get; protected set; }

    public event EventHandler<bool>? ProStatusChanged;
    public event EventHandler? ProLimitReached;

    public abstract Task RefreshAsync(CancellationToken ct);
    public abstract Task<ProUpgradeResult> PurchaseAsync(CancellationToken ct);
    public abstract Task<ProUpgradeResult> RestoreAsync(CancellationToken ct);

    public void NotifyProLimitReached()
    {
        if (IsProUnlocked || HasReachedFreeLimit)
            return;

        HasReachedFreeLimit = true;
        ProLimitReached?.Invoke(this, EventArgs.Empty);
    }

    protected void SetProUnlocked(bool value)
    {
        if (SettingsService.IsProUnlocked == value)
            return;

        SettingsService.IsProUnlocked = value;
        if (value)
            HasReachedFreeLimit = false;

        ProStatusChanged?.Invoke(this, value);
    }
}