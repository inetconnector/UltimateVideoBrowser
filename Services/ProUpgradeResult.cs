namespace UltimateVideoBrowser.Services;

public enum ProUpgradeStatus
{
    Success,
    Pending,
    Cancelled,
    Failed,
    NotSupported
}

public sealed record ProUpgradeResult(ProUpgradeStatus Status, string Message)
{
    public static ProUpgradeResult Success(string message)
    {
        return new ProUpgradeResult(ProUpgradeStatus.Success, message);
    }

    public static ProUpgradeResult Cancelled(string message)
    {
        return new ProUpgradeResult(ProUpgradeStatus.Cancelled, message);
    }

    public static ProUpgradeResult Pending(string message)
    {
        return new ProUpgradeResult(ProUpgradeStatus.Pending, message);
    }

    public static ProUpgradeResult Failed(string message)
    {
        return new ProUpgradeResult(ProUpgradeStatus.Failed, message);
    }

    public static ProUpgradeResult NotSupported(string message)
    {
        return new ProUpgradeResult(ProUpgradeStatus.NotSupported, message);
    }
}