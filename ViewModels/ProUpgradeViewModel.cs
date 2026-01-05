using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UltimateVideoBrowser.Resources.Strings;
using UltimateVideoBrowser.Services;

namespace UltimateVideoBrowser.ViewModels;

public partial class ProUpgradeViewModel : ObservableObject
{
    private readonly IDialogService dialogService;
    private readonly ILegalConsentService legalConsentService;
    private readonly IProUpgradeService proUpgradeService;
    [ObservableProperty] private bool isProBusy;
    [ObservableProperty] private bool isProLimitReached;
    [ObservableProperty] private bool isProUnlocked;
    [ObservableProperty] private string proLimitMessage = string.Empty;
    [ObservableProperty] private string proPriceText = string.Empty;
    [ObservableProperty] private string proStatusMessage = string.Empty;
    [ObservableProperty] private string proStatusTitle = string.Empty;

    public ProUpgradeViewModel(IProUpgradeService proUpgradeService, IDialogService dialogService,
        ILegalConsentService legalConsentService)
    {
        this.proUpgradeService = proUpgradeService;
        this.dialogService = dialogService;
        this.legalConsentService = legalConsentService;

        UpdateProStatus();
        _ = RefreshProStatusAsync();

        proUpgradeService.ProStatusChanged += (_, _) =>
            MainThread.BeginInvokeOnMainThread(UpdateProStatus);
        proUpgradeService.ProLimitReached += (_, _) =>
            MainThread.BeginInvokeOnMainThread(UpdateProLimitState);
    }

    private void UpdateProStatus()
    {
        IsProUnlocked = proUpgradeService.IsProUnlocked;
        ProStatusTitle = IsProUnlocked
            ? AppResources.SettingsProStatusUnlockedTitle
            : AppResources.SettingsProStatusFreeTitle;
        ProStatusMessage = IsProUnlocked
            ? AppResources.SettingsProUnlockedMessage
            : AppResources.SettingsProFreeMessage;
        ProPriceText = string.Format(AppResources.SettingsProPriceFormat,
            proUpgradeService.PriceText ?? AppResources.SettingsProPriceFallback);
        ProLimitMessage = string.Format(AppResources.SettingsProLimitReachedMessage,
            IProUpgradeService.FreePeopleLimit);
        UpdateProLimitState();
    }

    private void UpdateProLimitState()
    {
        IsProLimitReached = !IsProUnlocked && proUpgradeService.HasReachedFreeLimit;
    }

    private async Task RefreshProStatusAsync()
    {
        try
        {
            await proUpgradeService.RefreshAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort: keep UI responsive if billing cannot refresh.
        }

        await MainThread.InvokeOnMainThreadAsync(UpdateProStatus);
    }

    [RelayCommand]
    private async Task UpgradeToProAsync()
    {
        if (IsProBusy || IsProUnlocked)
            return;

        var consentAccepted = await legalConsentService.RequestProPurchaseConsentAsync(
            proUpgradeService.PriceText ?? AppResources.SettingsProPriceFallback,
            CancellationToken.None).ConfigureAwait(false);
        if (!consentAccepted)
            return;

        IsProBusy = true;
        try
        {
            var result = await proUpgradeService.PurchaseAsync(CancellationToken.None).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() => HandleProResultAsync(result));
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsProBusy = false);
        }
    }

    [RelayCommand]
    private async Task RestoreProAsync()
    {
        if (IsProBusy)
            return;

        IsProBusy = true;
        try
        {
            var result = await proUpgradeService.RestoreAsync(CancellationToken.None).ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() => HandleProResultAsync(result));
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsProBusy = false);
        }
    }

    private async Task HandleProResultAsync(ProUpgradeResult result)
    {
        string title;
        switch (result.Status)
        {
            case ProUpgradeStatus.Success:
                title = AppResources.SettingsProPurchaseSuccessTitle;
                UpdateProStatus();
                break;
            case ProUpgradeStatus.Cancelled:
                title = AppResources.SettingsProPurchaseCancelledTitle;
                break;
            case ProUpgradeStatus.Pending:
                title = AppResources.SettingsProPurchasePendingTitle;
                break;
            case ProUpgradeStatus.NotSupported:
                title = AppResources.SettingsProNotSupportedTitle;
                break;
            default:
                title = AppResources.SettingsProPurchaseFailedTitle;
                break;
        }

        await dialogService.DisplayAlertAsync(title, result.Message, AppResources.OkButton);
    }
}
