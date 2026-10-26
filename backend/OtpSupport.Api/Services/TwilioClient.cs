using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using OtpSupport.Api.Models;
namespace OtpSupport.Api.Services;
public sealed class TwilioClient(HttpClient http, IConfiguration cfg)
{
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private string AccountSid => Environment.GetEnvironmentVariable("TWILIO_ACCOUNT_SID") ?? cfg["Twilio:AccountSid"] ?? "";
    private string AuthToken => Environment.GetEnvironmentVariable("TWILIO_AUTH_TOKEN") ?? "";
    private string VerifySid => Environment.GetEnvironmentVariable("TWILIO_VERIFY_SERVICE_SID") ?? cfg["Twilio:VerifyServiceSid"] ?? "";
    public bool IsConfigured => AccountSid.StartsWith("AC", StringComparison.Ordinal) && AuthToken.Length > 0 && VerifySid.StartsWith("VA", StringComparison.Ordinal);
    private HttpRequestMessage Request(HttpMethod method, string url)
    {
        if (!IsConfigured) throw new TwilioException(503, "Twilio no configurado. Revise variables de entorno.");
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes($"{AccountSid}:{AuthToken}")));
        return req;
    }
    private async Task<JsonElement> Send(HttpRequestMessage req, CancellationToken ct)
    {
        using (req)
        using (var resp = await http.SendAsync(req, ct))
        {
            var body = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
            {
                string detail = "Twilio devolvió un error.";
                try { var j = JsonDocument.Parse(body).RootElement; detail = RedactSensitive(Get(j,"message") ?? detail); var code=Get(j,"code"); if (code != null) detail += $" (Twilio {code})"; } catch (JsonException) { }
                throw new TwilioException((int)resp.StatusCode, detail);
            }
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }
    }
    public async Task<TwilioMessage> Message(string sid, CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(sid, "^SM[a-fA-F0-9]{32}$")) throw new ArgumentException("Message SID inválido (SM + 32 caracteres hexadecimales).");
        var j = await Send(Request(HttpMethod.Get,$"https://api.twilio.com/2010-04-01/Accounts/{AccountSid}/Messages/{sid}.json"),ct);
        var from = Get(j,"from");
        return new(Get(j,"sid")??sid,Get(j,"status")??"unknown",Get(j,"error_code"),Get(j,"error_message"),MaskPhone(Get(j,"to")),MaskPhone(from),Get(j,"date_created"),Get(j,"date_sent"),Get(j,"date_updated"),from?.StartsWith("whatsapp:")==true?"whatsapp":"other");
    }
    public async Task<TwilioVerification> Verification(string sid, CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(sid,"^VE[a-fA-F0-9]{32}$")) throw new ArgumentException("Verification SID inválido (VE + 32 caracteres hexadecimales).");
        var j=await Send(Request(HttpMethod.Get,$"https://verify.twilio.com/v2/Services/{VerifySid}/Verifications/{sid}"),ct);
        return new(Get(j,"sid")??sid,Get(j,"status")??"unknown",MaskPhone(Get(j,"to")),Get(j,"channel"),Get(j,"date_created"),Get(j,"date_updated"));
    }
    public async Task<IReadOnlyList<TwilioVerificationAttempt>> VerificationAttempts(string phone, int? limit, CancellationToken ct)
    {
        phone = phone?.Trim() ?? "";
        if (!System.Text.RegularExpressions.Regex.IsMatch(phone, @"^\+[1-9][0-9]{7,14}$"))
            throw new ArgumentException("Teléfono inválido. Use formato E.164.");

        var normalizedLimit = limit ?? 3;
        if (normalizedLimit is not (3 or 5 or 10))
            throw new ArgumentException("El límite debe ser 3, 5 o 10.");

        var url = "https://verify.twilio.com/v2/Attempts"+
            $"?ChannelData.To={Uri.EscapeDataString(phone)}"+
            "&Channel=whatsapp"+
            $"&VerifyServiceSid={Uri.EscapeDataString(VerifySid)}"+
            $"&PageSize={normalizedLimit}";
        var root = await Send(Request(HttpMethod.Get, url), ct);
        if (!root.TryGetProperty("attempts", out var attempts) || attempts.ValueKind != JsonValueKind.Array)
            return Array.Empty<TwilioVerificationAttempt>();

        var mapped = new List<TwilioVerificationAttempt>();
        foreach (var attempt in attempts.EnumerateArray())
        {
            var hasChannelData = attempt.TryGetProperty("channel_data", out var channelData) && channelData.ValueKind == JsonValueKind.Object;
            string? ChannelValue(string property) => hasChannelData ? Get(channelData, property) : null;
            mapped.Add(new(
                Get(attempt, "sid") ?? "",
                Get(attempt, "verification_sid") ?? ChannelValue("verification_sid") ?? "",
                Get(attempt, "conversion_status") ?? "unknown",
                Get(attempt, "channel") ?? "unknown",
                Get(attempt, "date_created"),
                Get(attempt, "date_updated"),
                ChannelValue("message_status") ?? "unknown",
                ChannelValue("status") ?? "unknown",
                ChannelValue("error_code"),
                MaskPhone(ChannelValue("to"))));
        }

        return mapped
            .OrderByDescending(attempt => DateTimeOffset.TryParse(attempt.DateCreated, out var created) ? created : DateTimeOffset.MinValue)
            .Take(normalizedLimit)
            .ToArray();
    }
    public async Task<TwilioVerification> SendWhatsApp(string phone,CancellationToken ct)
    {
        var req=Request(HttpMethod.Post,$"https://verify.twilio.com/v2/Services/{VerifySid}/Verifications");
        req.Content=new FormUrlEncodedContent(new Dictionary<string,string>{{"To",phone},{"Channel","whatsapp"}});
        var j=await Send(req,ct);
        return new(Get(j,"sid")??"",Get(j,"status")??"unknown",MaskPhone(Get(j,"to")),Get(j,"channel"),Get(j,"date_created"),Get(j,"date_updated"));
    }
    private static string? Get(JsonElement e,string property) => e.TryGetProperty(property,out var v) && v.ValueKind!=JsonValueKind.Null ? v.ToString():null;
    private static string RedactSensitive(string value) => System.Text.RegularExpressions.Regex.Replace(value,@"\+[1-9][0-9]{7,14}",match=>MaskPhone(match.Value));
    public static string MaskPhone(string? phone)
    {
        if(string.IsNullOrEmpty(phone)) return "";
        var prefix=phone.StartsWith("whatsapp:")?"whatsapp:":"";
        var raw=phone[prefix.Length..];
        return prefix+(raw.Length<=5?"***":raw[..Math.Min(4,raw.Length)]+new string('*',Math.Max(0,raw.Length-6))+raw[^2..]);
    }
}
public sealed class TwilioException(int status,string message):Exception(message) { public int Status {get;}=status; }
