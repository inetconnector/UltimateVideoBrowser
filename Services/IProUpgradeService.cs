namespace UltimateVideoBrowser.Services;

public interface IProUpgradeService
{
    const int FreePeopleLimit = 20;
    string ProductId { get; }
    bool IsProUnlocked { get; }
    bool HasReachedFreeLimit { get; }
    string? PriceText { get; }
    event EventHandler<bool>? ProStatusChanged;
    event EventHandler? ProLimitReached;
    Task RefreshAsync(CancellationToken ct);
    Task<ProUpgradeResult> PurchaseAsync(CancellationToken ct);
    Task<ProUpgradeResult> RestoreAsync(CancellationToken ct);
    void NotifyProLimitReached();
}