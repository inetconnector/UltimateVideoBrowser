using System.Net;
using System.Text;
using System.Text.Json;
using UltimateVideoBrowser.LicenseServer.Models;

namespace UltimateVideoBrowser.LicenseServer.Services;

public static class LegalDocumentBuilder
{
    private const string BaseCss = "*{box-sizing:border-box}body.bg{margin:0;background:linear-gradient(135deg,#0f172a 0%,#111827 100%);color:#fff;font-family:system-ui,-apple-system,Segoe UI,Roboto,Ubuntu,\"Helvetica Neue\",Arial}.container{max-width:1100px;margin:80px auto;padding:0 16px}.lang{position:fixed;top:12px;right:16px;display:flex;gap:.5rem;z-index:1000}.lang .btn{padding:.35rem .6rem;border:1px solid #8a8a8a;border-radius:8px;text-decoration:none;color:#fff;background:rgba(255,255,255,.06)}.lang .btn.active{border-color:#fff;background:rgba(255,255,255,.18);font-weight:600}.hero{margin-top:20px;text-align:center}.hero h1{font-size:36px;margin:0 0 8px}.hero p{opacity:.85;margin:0}.cards{display:flex;flex-wrap:wrap;gap:18px;justify-content:center;margin-top:28px}.card{width:220px;height:170px;padding:16px;border-radius:14px;background:rgba(255,255,255,.06);border:1px solid rgba(255,255,255,.15);text-decoration:none;color:#fff;display:flex;flex-direction:column;align-items:center;justify-content:center;transition:.2s ease}.card:hover{transform:translateY(-2px);background:rgba(255,255,255,.12)}.card .icon{font-size:28px;margin-bottom:6px}.card .title{font-weight:700;margin-bottom:6px;text-align:center}.card .desc{font-size:12px;opacity:.9;text-align:center;line-height:1.35}.doc-card{width:min(900px,100%);height:auto;align-items:flex-start;text-align:left;gap:8px}.doc-card h2{margin:0 0 6px}.doc-card p,.doc-card li{font-size:14px;line-height:1.6;color:#f1f5f9}.doc-card ul{padding-left:18px;margin:8px 0}";

    public static string BuildImprint(LegalOptions options)
    {
        var body = new StringBuilder();
        body.AppendLine("<h2>Impressum</h2>");
        body.AppendLine("<p>Angaben gemäß §5 TMG &amp; §18 Abs. 2 MStV:</p>");
        body.AppendLine("<p>");
        body.AppendLine($"{Encode(options.ProviderName)}<br />");
        body.AppendLine($"{EncodeMultiline(options.Address)}<br />");
        body.AppendLine($"E-Mail: {Encode(options.Email)}<br />");
        body.AppendLine($"USt-IdNr.: {Encode(options.VatId)}");
        body.AppendLine("</p>");
        body.AppendLine("<p><strong>Verantwortlich für den Inhalt</strong><br />");
        body.AppendLine($"{EncodeMultiline(options.ResponsiblePerson)}</p>");
        return BuildPage("Impressum", "Rechtliche Angaben", body.ToString());
    }

    public static string BuildPrivacy(LegalOptions options)
    {
        var body = new StringBuilder();
        body.AppendLine("<h2>Datenschutzerklärung</h2>");
        body.AppendLine("<p><strong>Verantwortlicher</strong><br />");
        body.AppendLine($"{Encode(options.ProviderName)}<br />{EncodeMultiline(options.Address)}<br />");
        body.AppendLine($"E-Mail: {Encode(options.Email)}</p>");
        body.AppendLine("<p><strong>Verarbeitete Daten</strong></p>");
        body.AppendLine("<ul>");
        body.AppendLine("<li>E-Mail-Adresse, PayPal-Zahler-ID / Transaktions-ID, Zahlungsbetrag, IP-Adresse</li>");
        body.AppendLine("<li>Lizenzdaten (Lizenz-ID, Aktivierungstoken, Gerätefingerprint, Plattform)</li>");
        body.AppendLine("<li>Zeitpunkte von Kauf und Aktivierung</li>");
        body.AppendLine("</ul>");
        body.AppendLine("<p><strong>Zweck und Rechtsgrundlage</strong><br />");
        body.AppendLine("Vertragsabwicklung (Lizenzvergabe, Aktivierung, Support), Betrugsprävention und Missbrauchsschutz. ");
        body.AppendLine("Rechtsgrundlage: Art. 6 Abs. 1 lit. b DSGVO.</p>");
        body.AppendLine("<p><strong>Empfänger</strong><br />");
        body.AppendLine("PayPal (Europe) S.à r.l. et Cie, S.C.A. als Zahlungsabwickler sowie Hosting-Provider für den Lizenzserver.</p>");
        body.AppendLine("<p><strong>Speicherdauer</strong><br />");
        body.AppendLine("So lange wie für die Vertragsabwicklung und gesetzliche Aufbewahrungspflichten erforderlich.</p>");
        body.AppendLine("<p><strong>Betroffenenrechte</strong><br />");
        body.AppendLine("Auskunft, Berichtigung, Löschung, Einschränkung, Datenübertragbarkeit, Widerspruch sowie Beschwerderecht bei einer Aufsichtsbehörde.</p>");
        return BuildPage("Datenschutz", "Datenschutzinformationen", body.ToString());
    }

    public static string BuildTerms(LegalOptions options)
    {
        var body = new StringBuilder();
        body.AppendLine("<h2>Allgemeine Geschäftsbedingungen (AGB)</h2>");
        body.AppendLine("<p><strong>1. Geltungsbereich</strong><br />Diese AGB gelten für den Kauf von UltimateVideoBrowser Pro.</p>");
        body.AppendLine("<p><strong>2. Lizenz</strong><br />Die Lizenz ist nicht übertragbar und gilt für ein Gerät (max. Geräte pro Lizenz). ");
        body.AppendLine("Die Aktivierung erfordert eine Online-Prüfung; die Offline-Nutzung erfolgt über ein zeitlich begrenztes Aktivierungstoken.</p>");
        body.AppendLine("<p><strong>3. Zahlung</strong><br />Einmalzahlung über PayPal. Kein Abonnement.</p>");
        body.AppendLine("<p><strong>4. Updates und Verfügbarkeit</strong><br />Kein Anspruch auf zukünftige Funktionen oder fortlaufende Verfügbarkeit, soweit gesetzlich nicht anders geregelt.</p>");
        body.AppendLine("<p><strong>5. Haftung</strong><br />Haftung bei Vorsatz und grober Fahrlässigkeit. Bei einfacher Fahrlässigkeit nur für vorhersehbare, typische Schäden, soweit gesetzlich zulässig.</p>");
        body.AppendLine("<p><strong>6. Support und Rückerstattung</strong><br />");
        body.AppendLine($"Support-Kontakt: {Encode(options.SupportEmail)}. ");
        body.AppendLine("Rückerstattungen richten sich nach den gesetzlichen Widerrufsregeln für digitale Inhalte.</p>");
        return BuildPage("AGB", "Allgemeine Geschäftsbedingungen", body.ToString());
    }

    public static string BuildWithdrawal(LegalOptions options)
    {
        var body = new StringBuilder();
        body.AppendLine("<h2>Widerrufsrecht (digitale Inhalte)</h2>");
        body.AppendLine("<p>Du hast das Recht, binnen 14 Tagen ohne Angabe von Gründen diesen Vertrag zu widerrufen.</p>");
        body.AppendLine("<p>Ausnahme: Das Widerrufsrecht erlischt, wenn du ausdrücklich zustimmst, dass die Ausführung des Vertrags vor Ablauf der Widerrufsfrist beginnt, ");
        body.AppendLine("und du zur Kenntnis nimmst, dass du dein Widerrufsrecht verlierst.</p>");
        body.AppendLine($"<p>Zur Ausübung des Widerrufs kontaktiere: {Encode(options.SupportEmail)}.</p>");
        return BuildPage("Widerruf", "Widerrufsbelehrung", body.ToString());
    }

    public static LegalOptions LoadOptions(IConfiguration configuration)
    {
        var options = configuration.GetSection("Legal").Get<LegalOptions>() ?? new LegalOptions();
        var fileOptions = configuration.GetSection("LegalFile").Get<LegalFileOptions>() ?? new LegalFileOptions();
        if (!string.IsNullOrWhiteSpace(fileOptions.OptionsFilePath) && File.Exists(fileOptions.OptionsFilePath))
        {
            try
            {
                var json = File.ReadAllText(fileOptions.OptionsFilePath);
                var fileOptionsValue = JsonSerializer.Deserialize<LegalOptions>(json,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));
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

    private static string BuildPage(string title, string subtitle, string content)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html lang=\"de\"><head><meta charset=\"utf-8\" />");
        sb.AppendLine($"<title>{Encode(title)}</title>");
        sb.AppendLine($"<style>{BaseCss}</style></head>");
        sb.AppendLine("<body class=\"bg\">");
        sb.AppendLine("<div class=\"container\">");
        sb.AppendLine("<section class=\"hero\">");
        sb.AppendLine($"<h1>{Encode(title)}</h1>");
        sb.AppendLine($"<p>{Encode(subtitle)}</p>");
        sb.AppendLine("</section>");
        sb.AppendLine("<section class=\"cards\">");
        sb.AppendLine("<div class=\"card doc-card\">");
        sb.AppendLine(content);
        sb.AppendLine("</div>");
        sb.AppendLine("</section>");
        sb.AppendLine("</div>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
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
