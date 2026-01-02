namespace UltimateVideoBrowser.Services;

public enum ProUpgradeStatus
{
    Success,
    Cancelled,
    Failed,
    NotSupported
}

public sealed record ProUpgradeResult(ProUpgradeStatus Status, string Message)
{
    public static ProUpgradeResult Success(string message) => new(ProUpgradeStatus.Success, message);
    public static ProUpgradeResult Cancelled(string message) => new(ProUpgradeStatus.Cancelled, message);
    public static ProUpgradeResult Failed(string message) => new(ProUpgradeStatus.Failed, message);
    public static ProUpgradeResult NotSupported(string message) => new(ProUpgradeStatus.NotSupported, message);
}
