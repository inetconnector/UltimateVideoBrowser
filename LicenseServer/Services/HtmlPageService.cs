using System.Net;
using Microsoft.AspNetCore.WebUtilities;
using UltimateVideoBrowser.LicenseServer.Models;

namespace UltimateVideoBrowser.LicenseServer.Services;

public sealed class HtmlPageService
{
    private readonly IWebHostEnvironment environment;
    private readonly string htmlRoot;
    private readonly HashSet<string> landingLanguages;
    private readonly Dictionary<string, HashSet<string>> legalLanguages;

    public HtmlPageService(IWebHostEnvironment environment)
    {
        this.environment = environment;
        htmlRoot = Path.Combine(environment.ContentRootPath, "html");
        landingLanguages = LoadLandingLanguages();
        legalLanguages = LoadLegalLanguages();
    }

    public string LoadLandingPage(HttpRequest request)
    {
        var language = ResolveLanguage(request, "landing", landingLanguages, "de");
        var path = Path.Combine(htmlRoot, $"landing.{language}.html");
        if (!File.Exists(path))
            path = Path.Combine(htmlRoot, "landing.de.html");

        return File.ReadAllText(path);
    }

    public string LoadLegalPage(HttpRequest request, string documentKey, LegalOptions options)
    {
        legalLanguages.TryGetValue(documentKey, out var available);
        available ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var language = ResolveLanguage(request, documentKey, available, "de");
        var path = Path.Combine(htmlRoot, "legal", language, $"{documentKey}.html");
        if (!File.Exists(path))
            path = Path.Combine(htmlRoot, "legal", "de", $"{documentKey}.html");

        var html = File.ReadAllText(path);
        return ReplaceLegalPlaceholders(html, options);
    }

    private string ResolveLanguage(HttpRequest request, string key, HashSet<string> available, string fallback)
    {
        if (request.Query.TryGetValue("lang", out var queryLang))
        {
            var normalized = NormalizeLang(queryLang.ToString());
            if (available.Contains(normalized))
                return normalized;
        }

        var referrer = request.Headers.Referer.ToString();
        if (!string.IsNullOrWhiteSpace(referrer) && Uri.TryCreate(referrer, UriKind.Absolute, out var refererUri))
        {
            var query = QueryHelpers.ParseQuery(refererUri.Query);
            if (query.TryGetValue("lang", out var referrerLang))
            {
                var normalized = NormalizeLang(referrerLang.ToString());
                if (available.Contains(normalized))
                    return normalized;

                var prefix = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(prefix) && available.Contains(prefix))
                    return prefix;
            }
        }

        var header = request.Headers.AcceptLanguage.ToString();
        foreach (var lang in ParseAcceptLanguage(header))
        {
            var normalized = NormalizeLang(lang);
            if (available.Contains(normalized))
                return normalized;

            var prefix = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(prefix) && available.Contains(prefix))
                return prefix;
        }

        return fallback;
    }

    private HashSet<string> LoadLandingLanguages()
    {
        var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(htmlRoot))
            return languages;

        foreach (var file in Directory.GetFiles(htmlRoot, "landing.*.html"))
        {
            var parts = Path.GetFileName(file).Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3)
                languages.Add(parts[1]);
        }

        return languages;
    }

    private Dictionary<string, HashSet<string>> LoadLegalLanguages()
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var legalPath = Path.Combine(htmlRoot, "legal");
        if (!Directory.Exists(legalPath))
            return result;

        foreach (var directory in Directory.GetDirectories(legalPath))
        {
            var language = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(language))
                continue;

            foreach (var file in Directory.GetFiles(directory, "*.html"))
            {
                var key = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (!result.TryGetValue(key, out var languages))
                {
                    languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    result[key] = languages;
                }

                languages.Add(language);
            }
        }

        return result;
    }

    private static IEnumerable<string> ParseAcceptLanguage(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
            yield break;

        foreach (var part in header.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var lang = part.Split(';', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
            if (!string.IsNullOrWhiteSpace(lang))
                yield return lang;
        }
    }

    private static string NormalizeLang(string lang)
    {
        return lang.Trim().ToLowerInvariant();
    }

    private static string ReplaceLegalPlaceholders(string html, LegalOptions options)
    {
        return html
            .Replace("{{ProviderName}}", Encode(options.ProviderName))
            .Replace("{{Address}}", EncodeMultiline(options.Address))
            .Replace("{{Email}}", Encode(options.Email))
            .Replace("{{VatId}}", Encode(options.VatId))
            .Replace("{{ResponsiblePerson}}", EncodeMultiline(options.ResponsiblePerson))
            .Replace("{{SupportEmail}}", Encode(options.SupportEmail));
    }

    private static string Encode(string value)
    {
        return WebUtility.HtmlEncode(value ?? string.Empty);
    }

    private static string EncodeMultiline(string value)
    {
        var encoded = WebUtility.HtmlEncode(value ?? string.Empty);
        return encoded.Replace("\n", "<br />");
    }
}
