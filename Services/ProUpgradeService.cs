using UltimateVideoBrowser.Resources.Strings;

namespace UltimateVideoBrowser.Services;

public sealed class ProUpgradeService : ProUpgradeServiceBase
{
    private readonly IDeviceFingerprintService deviceFingerprintService;
    private readonly IDialogService dialogService;
    private readonly LicenseServerClient licenseServerClient;

    public ProUpgradeService(
        AppSettingsService settingsService,
        LicenseServerClient licenseServerClient,
        IDeviceFingerprintService deviceFingerprintService,
        IDialogService dialogService)
        : base(settingsService)
    {
        this.licenseServerClient = licenseServerClient;
        this.deviceFingerprintService = deviceFingerprintService;
        this.dialogService = dialogService;
        PriceText = AppResources.SettingsProPriceFallback;
    }

    public override string ProductId => "ultimatevideobrowser_pro";

    public override async Task RefreshAsync(CancellationToken ct)
    {
        RefreshActivationStatus();
        var pricing = await licenseServerClient.GetPricingAsync(ct).ConfigureAwait(false);
        if (pricing?.Price is not null)
            PriceText = $"{pricing.Price.Value} {pricing.Price.Currency}";
    }

    public override async Task<ProUpgradeResult> PurchaseAsync(CancellationToken ct)
    {
        var checkout = await licenseServerClient.CreateCheckoutAsync(ct).ConfigureAwait(false);
        if (checkout?.CheckoutUrl == null)
            return ProUpgradeResult.Failed(AppResources.SettingsProPurchaseFailedMessage);

        if (!await Launcher.OpenAsync(new Uri(checkout.CheckoutUrl)).ConfigureAwait(false))
            return ProUpgradeResult.Failed(AppResources.SettingsProPurchaseFailedMessage);

        return ProUpgradeResult.Pending(AppResources.SettingsProPurchasePendingMessage);
    }

    public override async Task<ProUpgradeResult> RestoreAsync(CancellationToken ct)
    {
        var licenseKey = await dialogService.DisplayPromptAsync(
            AppResources.SettingsProActivateTitle,
            AppResources.SettingsProActivateMessage,
            AppResources.SettingsProActivateAccept,
            AppResources.CancelButton,
            AppResources.SettingsProActivatePlaceholder,
            512,
            Keyboard.Text).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(licenseKey))
            return ProUpgradeResult.Cancelled(AppResources.SettingsProPurchaseCancelledMessage);

        var fingerprint = await deviceFingerprintService.GetFingerprintAsync(ct).ConfigureAwait(false);
        var platform = DeviceInfo.Platform.ToString().ToLowerInvariant();
        var response = await licenseServerClient.ActivateAsync(
            new ActivateRequest(licenseKey.Trim(), fingerprint, platform),
            ct).ConfigureAwait(false);

        if (response == null || !string.Equals(response.Status, "activated", StringComparison.OrdinalIgnoreCase))
            return ProUpgradeResult.Failed(AppResources.SettingsProRestoreFailedMessage);

        SettingsService.ProActivationToken = response.ActivationToken ?? string.Empty;
        SettingsService.ProActivationValidUntil = response.ValidUntil;
        SetProUnlocked(true);
        return ProUpgradeResult.Success(AppResources.SettingsProRestoreSuccessMessage);
    }

    private void RefreshActivationStatus()
    {
        var validUntil = SettingsService.ProActivationValidUntil;
        if (validUntil.HasValue && validUntil <= DateTimeOffset.UtcNow)
        {
            SettingsService.ProActivationToken = string.Empty;
            SettingsService.ProActivationValidUntil = null;
            SetProUnlocked(false);
            return;
        }

        if (!string.IsNullOrWhiteSpace(SettingsService.ProActivationToken))
            SetProUnlocked(true);
        else if (IsProUnlocked)
            SetProUnlocked(false);
    }
}