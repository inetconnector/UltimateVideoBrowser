#if ANDROID
using UltimateVideoBrowser.Resources.Strings;
using Application = Android.App.Application;
using Object = Java.Lang.Object;

namespace UltimateVideoBrowser.Services;

public sealed class PlayBillingProUpgradeService : ProUpgradeServiceBase
{
    private const string OneTimeProductId = "photoapp_pro_unlock";
    private const string InAppProductType = "inapp";
    private readonly PurchasesUpdatedListener purchasesUpdatedListener;
    private readonly object sync = new();
    private BillingClient? billingClient;
    private ProductDetails? cachedProduct;
    private TaskCompletionSource<ProUpgradeResult>? purchaseTcs;

    public PlayBillingProUpgradeService(AppSettingsService settingsService)
        : base(settingsService)
    {
        PriceText = AppResources.SettingsProPriceFallback;
        purchasesUpdatedListener = new PurchasesUpdatedListener(this);
    }

    public override string ProductId => OneTimeProductId;

    public override async Task RefreshAsync(CancellationToken ct)
    {
        var client = await EnsureConnectedAsync(ct).ConfigureAwait(false);
        await RefreshProductDetailsAsync(client, ct).ConfigureAwait(false);
        await RefreshPurchasesAsync(client, ct).ConfigureAwait(false);
    }

    public override async Task<ProUpgradeResult> PurchaseAsync(CancellationToken ct)
    {
        var activity = Platform.CurrentActivity;
        if (activity == null)
            return ProUpgradeResult.Failed(AppResources.SettingsProPurchaseFailedMessage);

        var client = await EnsureConnectedAsync(ct).ConfigureAwait(false);
        var details = cachedProduct ?? await RefreshProductDetailsAsync(client, ct).ConfigureAwait(false);
        if (details == null)
            return ProUpgradeResult.Failed(AppResources.SettingsProPurchaseFailedMessage);

        var param = BillingFlowParams.ProductDetailsParams.NewBuilder()
            .SetProductDetails(details)
            .Build();

        var flowParams = BillingFlowParams.NewBuilder()
            .SetProductDetailsParamsList(new List<BillingFlowParams.ProductDetailsParams> { param })
            .Build();

        var launchResult = client.LaunchBillingFlow(activity, flowParams);
        if (launchResult.ResponseCode != BillingResponseCode.Ok)
            return ProUpgradeResult.Failed(AppResources.SettingsProPurchaseFailedMessage);

        purchaseTcs = new TaskCompletionSource<ProUpgradeResult>();
        using var _ = ct.Register(() => purchaseTcs.TrySetCanceled());
        return await purchaseTcs.Task.ConfigureAwait(false);
    }

    public override async Task<ProUpgradeResult> RestoreAsync(CancellationToken ct)
    {
        var client = await EnsureConnectedAsync(ct).ConfigureAwait(false);
        var restored = await RefreshPurchasesAsync(client, ct).ConfigureAwait(false);
        return restored
            ? ProUpgradeResult.Success(AppResources.SettingsProRestoreSuccessMessage)
            : ProUpgradeResult.Failed(AppResources.SettingsProRestoreFailedMessage);
    }

    private void OnPurchasesUpdated(BillingResult billingResult, IList<Purchase>? purchases)
    {
        if (billingResult.ResponseCode == BillingResponseCode.UserCancelled)
        {
            purchaseTcs?.TrySetResult(ProUpgradeResult.Cancelled(AppResources.SettingsProPurchaseCancelledMessage));
            return;
        }

        if (billingResult.ResponseCode != BillingResponseCode.Ok)
        {
            purchaseTcs?.TrySetResult(ProUpgradeResult.Failed(AppResources.SettingsProPurchaseFailedMessage));
            return;
        }

        var matched = purchases?
            .FirstOrDefault(p => p.Products.Contains(ProductId));

        if (matched == null)
        {
            purchaseTcs?.TrySetResult(ProUpgradeResult.Failed(AppResources.SettingsProPurchaseFailedMessage));
            return;
        }

        _ = HandlePurchaseAsync(matched, CancellationToken.None);
    }

    private async Task<BillingClient> EnsureConnectedAsync(CancellationToken ct)
    {
        lock (sync)
        {
            if (billingClient?.IsReady == true)
                return billingClient;
        }

        var context = Application.Context;
        var client = BillingClient.NewBuilder(context)
            .SetListener(purchasesUpdatedListener)
            .EnablePendingPurchases()
            .Build();

        var tcs = new TaskCompletionSource<BillingResult>();
        client.StartConnection(new BillingStateListener(tcs));
        using var _ = ct.Register(() => tcs.TrySetCanceled());
        var result = await tcs.Task.ConfigureAwait(false);

        if (result.ResponseCode != BillingResponseCode.Ok)
            throw new InvalidOperationException("Unable to connect to Google Play Billing.");

        lock (sync)
        {
            billingClient = client;
        }

        return client;
    }

    private async Task<ProductDetails?> RefreshProductDetailsAsync(BillingClient client, CancellationToken ct)
    {
        var product = QueryProductDetailsParams.Product.NewBuilder()
            .SetProductId(ProductId)
            .SetProductType(InAppProductType)
            .Build();

        var queryParams = QueryProductDetailsParams.NewBuilder()
            .SetProductList(new List<QueryProductDetailsParams.Product> { product })
            .Build();

        var result = await client.QueryProductDetailsAsync(queryParams).ConfigureAwait(false);

        var billingResult = ExtractBillingResult(result);
        if (billingResult == null || billingResult.ResponseCode != BillingResponseCode.Ok)
            return cachedProduct;

        cachedProduct = ExtractProductDetailsList(result)?.FirstOrDefault();
        var offerDetails = cachedProduct?.GetOneTimePurchaseOfferDetails();
        if (offerDetails != null)
            PriceText = offerDetails.FormattedPrice;

        return cachedProduct;
    }

    private async Task<bool> RefreshPurchasesAsync(BillingClient client, CancellationToken ct)
    {
        var queryParams = QueryPurchasesParams.NewBuilder()
            .SetProductType(InAppProductType)
            .Build();

        var result = await client.QueryPurchasesAsync(queryParams).ConfigureAwait(false);

        var billingResult = ExtractBillingResult(result);
        if (billingResult == null || billingResult.ResponseCode != BillingResponseCode.Ok)
            return false;

        var purchase = ExtractPurchasesList(result)?
            .FirstOrDefault(p => p.Products.Contains(ProductId) && p.PurchaseState == PurchaseState.Purchased);

        if (purchase == null)
            return false;

        await HandlePurchaseAsync(purchase, ct).ConfigureAwait(false);
        return true;
    }

    private async Task HandlePurchaseAsync(Purchase purchase, CancellationToken ct)
    {
        if (!purchase.IsAcknowledged)
        {
            var ackParams = AcknowledgePurchaseParams.NewBuilder()
                .SetPurchaseToken(purchase.PurchaseToken)
                .Build();

            var ackTcs = new TaskCompletionSource<BillingResult>();
            var client = billingClient;
            if (client != null)
                client.AcknowledgePurchase(ackParams, new AckListener(ackTcs));

            using var _ = ct.Register(() => ackTcs.TrySetCanceled());
            var ackResult = await ackTcs.Task.ConfigureAwait(false);
            if (ackResult.ResponseCode != BillingResponseCode.Ok)
            {
                purchaseTcs?.TrySetResult(ProUpgradeResult.Failed(AppResources.SettingsProPurchaseFailedMessage));
                return;
            }
        }

        SetProUnlocked(true);
        purchaseTcs?.TrySetResult(ProUpgradeResult.Success(AppResources.SettingsProPurchaseSuccessMessage));
    }

    private static BillingResult? ExtractBillingResult(object? result)
    {
        if (result == null)
            return null;

        var type = result.GetType();
        var prop = type.GetProperty("BillingResult");
        if (prop?.GetValue(result) is BillingResult billingResult)
            return billingResult;

        var method = type.GetMethod("GetBillingResult", Type.EmptyTypes);
        if (method?.Invoke(result, null) is BillingResult methodResult)
            return methodResult;

        return null;
    }

    private static IList<ProductDetails>? ExtractProductDetailsList(object? result)
    {
        if (result == null)
            return null;

        var type = result.GetType();
        var prop = type.GetProperty("ProductDetailsList");
        if (prop?.GetValue(result) is IList<ProductDetails> list)
            return list;

        var method = type.GetMethod("GetProductDetailsList", Type.EmptyTypes);
        if (method?.Invoke(result, null) is IList<ProductDetails> methodList)
            return methodList;

        return null;
    }

    private static IList<Purchase>? ExtractPurchasesList(object? result)
    {
        if (result == null)
            return null;

        var type = result.GetType();
        var prop = type.GetProperty("PurchasesList");
        if (prop?.GetValue(result) is IList<Purchase> list)
            return list;

        var method = type.GetMethod("GetPurchasesList", Type.EmptyTypes);
        if (method?.Invoke(result, null) is IList<Purchase> methodList)
            return methodList;

        return null;
    }

    private sealed class BillingStateListener : Object, IBillingClientStateListener
    {
        private readonly TaskCompletionSource<BillingResult> tcs;

        public BillingStateListener(TaskCompletionSource<BillingResult> tcs)
        {
            this.tcs = tcs;
        }

        public void OnBillingSetupFinished(BillingResult billingResult)
        {
            tcs.TrySetResult(billingResult);
        }

        public void OnBillingServiceDisconnected()
        {
            tcs.TrySetResult(BillingResult.NewBuilder()
                .SetResponseCode((int)BillingResponseCode.ServiceDisconnected)
                .Build());
        }
    }

    private sealed class AckListener : Object, IAcknowledgePurchaseResponseListener
    {
        private readonly TaskCompletionSource<BillingResult> tcs;

        public AckListener(TaskCompletionSource<BillingResult> tcs)
        {
            this.tcs = tcs;
        }

        public void OnAcknowledgePurchaseResponse(BillingResult billingResult)
        {
            tcs.TrySetResult(billingResult);
        }
    }

    private sealed class PurchasesUpdatedListener : Object, IPurchasesUpdatedListener
    {
        private readonly WeakReference<PlayBillingProUpgradeService> serviceRef;

        public PurchasesUpdatedListener(PlayBillingProUpgradeService service)
        {
            serviceRef = new WeakReference<PlayBillingProUpgradeService>(service);
        }

        public void OnPurchasesUpdated(BillingResult billingResult, IList<Purchase>? purchases)
        {
            if (serviceRef.TryGetTarget(out var service))
                service.OnPurchasesUpdated(billingResult, purchases);
        }
    }
}
#endif