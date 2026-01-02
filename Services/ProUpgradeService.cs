using UltimateVideoBrowser.Resources.Strings;

namespace UltimateVideoBrowser.Services;

public sealed class ProUpgradeService : ProUpgradeServiceBase
{
    public ProUpgradeService(AppSettingsService settingsService)
        : base(settingsService)
    {
        PriceText = AppResources.SettingsProPriceFallback;
    }

    public override string ProductId => "photoapp_pro_unlock";

    public override Task RefreshAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public override Task<ProUpgradeResult> PurchaseAsync(CancellationToken ct)
    {
        return Task.FromResult(ProUpgradeResult.NotSupported(AppResources.SettingsProNotSupportedMessage));
    }

    public override Task<ProUpgradeResult> RestoreAsync(CancellationToken ct)
    {
        return Task.FromResult(ProUpgradeResult.NotSupported(AppResources.SettingsProNotSupportedMessage));
    }
}