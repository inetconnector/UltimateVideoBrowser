using System.Text.Json;
using UltimateVideoBrowser.LicenseServer.Models;

namespace UltimateVideoBrowser.LicenseServer.Services;

public static class OptionsLoader
{
    public static T LoadOptions<T>(IConfiguration configuration, string sectionName, string fileSectionName)
        where T : class, new()
    {
        var options = configuration.GetSection(sectionName).Get<T>() ?? new T();
        var fileOptions = configuration.GetSection(fileSectionName).Get<OptionsFilePathOptions>()
            ?? new OptionsFilePathOptions();
        if (!string.IsNullOrWhiteSpace(fileOptions.OptionsFilePath) && File.Exists(fileOptions.OptionsFilePath))
        {
            try
            {
                var json = File.ReadAllText(fileOptions.OptionsFilePath);
                var fileOptionsValue = JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if (fileOptionsValue != null)
                    options = fileOptionsValue;
            }
            catch
            {
                // Best-effort: keep appsettings values if file is unavailable or invalid.
            }
        }

        return options;
    }
}
