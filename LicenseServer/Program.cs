using UltimateVideoBrowser.LicenseServer.Models;
using UltimateVideoBrowser.LicenseServer.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<FileDataStore>();
builder.Services.AddSingleton<LicenseService>();

var app = builder.Build();

app.MapGet("/api/pricing", (LicenseService licenseService) =>
{
    return Results.Ok(licenseService.GetPricing());
});

app.MapPost("/api/checkout", (LicenseService licenseService) =>
{
    return Results.Ok(licenseService.CreateCheckout());
});

app.MapGet("/impressum", (IConfiguration configuration) =>
{
    var options = LegalDocumentBuilder.LoadOptions(configuration);
    var html = LegalDocumentBuilder.BuildImprint(options);
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapGet("/datenschutz", (IConfiguration configuration) =>
{
    var options = LegalDocumentBuilder.LoadOptions(configuration);
    var html = LegalDocumentBuilder.BuildPrivacy(options);
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapGet("/agb", (IConfiguration configuration) =>
{
    var options = LegalDocumentBuilder.LoadOptions(configuration);
    var html = LegalDocumentBuilder.BuildTerms(options);
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapGet("/widerruf", (IConfiguration configuration) =>
{
    var options = LegalDocumentBuilder.LoadOptions(configuration);
    var html = LegalDocumentBuilder.BuildWithdrawal(options);
    return Results.Content(html, "text/html; charset=utf-8");
});

app.MapPost("/api/paypal/webhook", async (
    PayPalWebhookRequest request,
    LicenseService licenseService,
    FileDataStore dataStore,
    IConfiguration configuration,
    CancellationToken ct) =>
{
    if (!string.Equals(request.PaymentStatus, "COMPLETED", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = "Payment not completed." });

    var licenseOptions = configuration.GetSection("License").Get<LicenseOptions>() ?? new LicenseOptions();
    var createdAt = DateTimeOffset.UtcNow;
    var signed = licenseService.IssueLicense("any", "unbound", createdAt, null);

    var record = new PurchaseRecord
    {
        OrderId = request.OrderId,
        PaymentProvider = "paypal",
        PaymentStatus = request.PaymentStatus,
        Amount = request.Amount,
        Product = new ProductInfo(licenseOptions.ProductId, licenseOptions.ProductName, "1.x"),
        Buyer = new BuyerInfo(request.PayerId, licenseService.HashEmail(request.Email, licenseOptions.HmacSecret)),
        License = new LicenseInfo
        {
            LicenseId = signed.Payload.LicenseId,
            Type = "per_device",
            Platforms = new[] { "android", "windows" },
            MaxDevices = licenseOptions.MaxDevices,
            CreatedAt = createdAt,
            ExpiresAt = null
        }
    };

    var licenseRecord = new LicenseRecord
    {
        LicenseId = signed.Payload.LicenseId,
        ProductId = licenseOptions.ProductId,
        ProductName = licenseOptions.ProductName,
        MaxDevices = licenseOptions.MaxDevices,
        Platforms = new[] { "android", "windows" },
        CreatedAt = createdAt,
        ExpiresAt = null,
        DeviceFingerprint = null
    };

    await dataStore.SavePurchaseRecordAsync(record, ct).ConfigureAwait(false);
    await dataStore.SaveLicenseRecordAsync(licenseRecord, ct).ConfigureAwait(false);

    return Results.Ok(new { licenseKey = signed.LicenseKey });
});

app.MapPost("/api/licenses/activate", async (
    ActivateRequest request,
    LicenseService licenseService,
    FileDataStore dataStore,
    IConfiguration configuration,
    CancellationToken ct) =>
{
    if (!licenseService.TryValidateLicense(request.LicenseKey, out var payload) || payload == null)
        return Results.BadRequest(new ActivationResponse("invalid", null, null, null));

    var options = configuration.GetSection("License").Get<LicenseOptions>() ?? new LicenseOptions();
    if (!string.Equals(payload.Product, options.ProductId, StringComparison.Ordinal))
        return Results.BadRequest(new ActivationResponse("invalid", null, null, null));

    if (!string.Equals(payload.Platform, "any", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(payload.Platform, request.Platform, StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new ActivationResponse("platform_mismatch", null, null, null));

    var licenseRecord = await dataStore.GetLicenseRecordAsync(payload.LicenseId, ct).ConfigureAwait(false);
    if (licenseRecord == null)
        return Results.BadRequest(new ActivationResponse("not_found", null, null, null));

    if (licenseRecord.ExpiresAt.HasValue && licenseRecord.ExpiresAt <= DateTimeOffset.UtcNow)
        return Results.BadRequest(new ActivationResponse("expired", null, null, null));

    if (!string.IsNullOrWhiteSpace(licenseRecord.DeviceFingerprint)
        && !string.Equals(licenseRecord.DeviceFingerprint, request.DeviceFingerprint, StringComparison.Ordinal))
        return Results.BadRequest(new ActivationResponse("device_mismatch", null, null, null));

    licenseRecord.DeviceFingerprint = request.DeviceFingerprint;
    await dataStore.SaveLicenseRecordAsync(licenseRecord, ct).ConfigureAwait(false);

    var issuedAt = DateTimeOffset.UtcNow;
    var validUntil = issuedAt.AddDays(options.ActivationDays);
    var activationPayload = new ActivationTokenPayload
    {
        LicenseId = payload.LicenseId,
        DeviceFingerprint = request.DeviceFingerprint,
        IssuedAt = issuedAt.ToUnixTimeSeconds(),
        ExpiresAt = validUntil.ToUnixTimeSeconds()
    };

    var activationToken = licenseService.CreateActivationToken(activationPayload);
    return Results.Ok(new ActivationResponse("activated", activationToken, validUntil, new[] { "pro" }));
});

app.Run();
