using System.Text.Json;
using UltimateVideoBrowser.Helpers;

namespace UltimateVideoBrowser.Services;

public sealed record NetworkShareCredentials(string Username, string Password);

public sealed class NetworkShareCredentialStore
{
    private const string KeyPrefix = "network_share_";

    public async Task<NetworkShareCredentials?> GetAsync(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            return null;

        try
        {
            var payload = await SecureStorage.Default.GetAsync(KeyPrefix + sourceId);
            if (string.IsNullOrWhiteSpace(payload))
                return null;

            return JsonSerializer.Deserialize<NetworkShareCredentials>(payload);
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "NetworkShareCredentialStore.GetAsync");
            return null;
        }
    }

    public async Task<bool> SaveAsync(string sourceId, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            return false;

        try
        {
            var payload = JsonSerializer.Serialize(new NetworkShareCredentials(username, password));
            await SecureStorage.Default.SetAsync(KeyPrefix + sourceId, payload);
            return true;
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "NetworkShareCredentialStore.SaveAsync");
            return false;
        }
    }

    public void Remove(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            return;

        try
        {
            SecureStorage.Default.Remove(KeyPrefix + sourceId);
        }
        catch (Exception ex)
        {
            ErrorLog.LogException(ex, "NetworkShareCredentialStore.Remove");
        }
    }
}
