using System.Security.Cryptography;
using System.Text;

namespace UltimateVideoBrowser.Services;

public static class DeviceFingerprintHasher
{
    public static string Hash(string input)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}