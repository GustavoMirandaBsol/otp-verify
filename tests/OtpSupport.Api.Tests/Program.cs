using OtpSupport.Api.Models;
using OtpSupport.Api.Services;
using OtpSupport.Api.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;

static void Equal<T>(T expected, T actual, string test)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"FAIL {test}: expected '{expected}', got '{actual}'.");
}

static void Contains(string expected, string actual, string test)
{
    if (!actual.Contains(expected, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException($"FAIL {test}: '{actual}' does not contain '{expected}'.");
}

static void DoesNotContain(string unexpected, string actual, string test)
{
    if (actual.Contains(unexpected, StringComparison.Ordinal))
        throw new InvalidOperationException($"FAIL {test}: sensitive value '{unexpected}' was present.");
}

static async Task ThrowsAsync<TException>(Func<Task> action, string test) where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"FAIL {test}: expected {typeof(TException).Name}.");
}

static (TwilioClient Client, RecordingHandler Handler) AttemptClient(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
{
    var handler = new RecordingHandler(json, statusCode);
    var configuration = new ConfigurationBuilder().Build();
    return (new TwilioClient(new HttpClient(handler), configuration), handler);
}

static TwilioVerificationAttempt Attempt(
    string conversionStatus,
    string messageStatus,
    string? errorCode = null) =>
    new(
        "VL11111111111111111111111111111111",
        "VE22222222222222222222222222222222",
        conversionStatus,
        "whatsapp",
        "2026-10-24T12:00:00Z",
        "2026-10-24T12:01:00Z",
        messageStatus,
        "unconfirmed",
        errorCode,
        "+5917*****00");

var converted = DiagnosisService.ForAttempt(Attempt("converted", "delivered"));
Equal("verification-approved", converted.Category, "converted category");
Equal("success", converted.Severity, "converted severity");
Contains("confirmada", converted.Explanation, "converted explanation");

var delivered = DiagnosisService.ForAttempt(Attempt("unconverted", "delivered"));
Equal("delivery-confirmed", delivered.Category, "delivered category");
Equal("success", delivered.Severity, "delivered severity");
Contains("no equivale", delivered.Explanation, "delivery is not approval");

var pending = DiagnosisService.ForAttempt(Attempt("unconverted", "queued"));
Equal("delivery-pending", pending.Category, "pending category");
Equal("warning", pending.Severity, "pending severity");

var failed = DiagnosisService.ForAttempt(Attempt("unconverted", "undelivered", "30008"));
Equal("delivery-failed", failed.Category, "failed category");
Equal("error", failed.Severity, "failed severity");
Contains("30008", failed.Explanation, "failed error code");

Console.WriteLine("PASS: diagnósticos de intentos verificados.");

var capturedLogs = new List<string>();
var loggingServices = new ServiceCollection();
loggingServices.AddLogging(logging =>
{
    SensitiveLogging.Configure(logging);
    logging.AddProvider(new CapturingLoggerProvider(capturedLogs));
});
using (var loggingProvider = loggingServices.BuildServiceProvider())
{
    var loggerFactory = loggingProvider.GetRequiredService<ILoggerFactory>();
    loggerFactory.CreateLogger("Microsoft.AspNetCore.Hosting.Diagnostics").LogCritical("Incoming ?phone={Phone}", "+59170000000");
    loggerFactory.CreateLogger("System.Net.Http.HttpClient.OtpSupport.Api.Services.TwilioClient.LogicalHandler").LogCritical("Outgoing {Uri}", "https://verify.twilio.com/v2/Attempts?ChannelData.To=%2B59170000000");
    Equal(0, capturedLogs.Count, "sensitive framework URL logs disabled");
    loggerFactory.CreateLogger("OtpSupport.Api.Application").LogWarning("Safe application warning");
    Equal(1, capturedLogs.Count, "application logs remain enabled");
}
Console.WriteLine("PASS: redacción de logs HTTP verificada.");

Environment.SetEnvironmentVariable("TWILIO_ACCOUNT_SID", "ACaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
Environment.SetEnvironmentVariable("TWILIO_AUTH_TOKEN", "test-token");
Environment.SetEnvironmentVariable("TWILIO_VERIFY_SERVICE_SID", "VAbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

try
{
    const string attemptsJson = """
    {
      "attempts": [
        {
          "sid": "VL11111111111111111111111111111111",
          "account_sid": "ACaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          "service_sid": "VAbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
          "verification_sid": "VE11111111111111111111111111111111",
          "date_created": "2026-10-22T10:00:00Z",
          "date_updated": "2026-10-22T10:01:00Z",
          "conversion_status": "unconverted",
          "channel": "whatsapp",
          "price": { "value": "0.01", "currency": "USD" },
          "channel_data": { "verification_sid": "VE11111111111111111111111111111111", "to": "+59170000000", "status": "unconfirmed", "message_status": "queued", "error_code": null },
          "url": "https://verify.twilio.com/v2/Attempts/VL11111111111111111111111111111111"
        },
        {
          "sid": "VL33333333333333333333333333333333",
          "account_sid": "ACaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          "service_sid": "VAbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
          "verification_sid": "VE33333333333333333333333333333333",
          "date_created": "2026-10-24T10:00:00Z",
          "date_updated": "2026-10-24T10:01:00Z",
          "conversion_status": "converted",
          "channel": "whatsapp",
          "price": { "value": "0.01", "currency": "USD" },
          "channel_data": { "verification_sid": "VE33333333333333333333333333333333", "to": "+59170000000", "status": "confirmed", "message_status": "delivered", "error_code": null },
          "url": "https://verify.twilio.com/v2/Attempts/VL33333333333333333333333333333333"
        },
        {
          "sid": "VL22222222222222222222222222222222",
          "account_sid": "ACaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          "service_sid": "VAbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
          "verification_sid": "VE22222222222222222222222222222222",
          "date_created": "2026-10-23T10:00:00Z",
          "date_updated": "2026-10-23T10:01:00Z",
          "conversion_status": "unconverted",
          "channel": "whatsapp",
          "price": { "value": "0.01", "currency": "USD" },
          "channel_data": { "verification_sid": "VE22222222222222222222222222222222", "to": "+59170000000", "status": "unconfirmed", "message_status": "undelivered", "error_code": "30008" },
          "url": "https://verify.twilio.com/v2/Attempts/VL22222222222222222222222222222222"
        },
        {
          "sid": "VL00000000000000000000000000000000",
          "account_sid": "ACaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
          "service_sid": "VAbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
          "verification_sid": "VE00000000000000000000000000000000",
          "date_created": "not-a-date",
          "date_updated": null,
          "conversion_status": "unconverted",
          "channel": "whatsapp",
          "price": null,
          "channel_data": { "verification_sid": "VE00000000000000000000000000000000", "to": "+59170000000", "status": "unconfirmed", "message_status": "failed", "error_code": "30005" },
          "url": "https://verify.twilio.com/v2/Attempts/VL00000000000000000000000000000000"
        }
      ],
      "meta": { "page": 0, "page_size": 3, "next_page_url": null, "key": "attempts" }
    }
    """;

    var (client, handler) = AttemptClient(attemptsJson);
    var attempts = await client.VerificationAttempts("+59170000000", 3, CancellationToken.None);
    Equal(3, attempts.Count, "attempt result limit");
    Equal("VL33333333333333333333333333333333", attempts[0].Sid, "newest attempt first");
    Equal("VL22222222222222222222222222222222", attempts[1].Sid, "second newest attempt");
    Equal("VL11111111111111111111111111111111", attempts[2].Sid, "third newest attempt");
    Equal("+591******00", attempts[0].To, "attempt phone masked");
    Contains("/v2/Attempts?", handler.LastRequestUri!, "attempt endpoint");
    Contains("ChannelData.To=%2B59170000000", handler.LastRequestUri!, "destination filter");
    Contains("VerifyServiceSid=VAbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", handler.LastRequestUri!, "service filter");
    Contains("Channel=whatsapp", handler.LastRequestUri!, "channel filter");
    Contains("PageSize=3", handler.LastRequestUri!, "page size filter");

    var (defaultClient, defaultHandler) = AttemptClient("{\"attempts\":[]}");
    var empty = await defaultClient.VerificationAttempts("+59170000000", null, CancellationToken.None);
    Equal(0, empty.Count, "empty attempts");
    Contains("PageSize=3", defaultHandler.LastRequestUri!, "default page size");

    const string missingFieldsJson = """
    { "attempts": [{ "sid": "VL44444444444444444444444444444444", "verification_sid": "VE44444444444444444444444444444444", "conversion_status": "unconverted", "channel": "whatsapp", "date_created": null, "date_updated": null }] }
    """;
    var (missingClient, _) = AttemptClient(missingFieldsJson);
    var missing = await missingClient.VerificationAttempts("+59170000000", 5, CancellationToken.None);
    Equal("unknown", missing[0].MessageStatus, "missing message status");
    Equal("unknown", missing[0].AttemptStatus, "missing attempt status");
    Equal("", missing[0].To, "missing destination");

    var (validationClient, validationHandler) = AttemptClient("{\"attempts\":[]}");
    await ThrowsAsync<ArgumentException>(() => validationClient.VerificationAttempts("59170000000", 3, CancellationToken.None), "invalid E.164");
    await ThrowsAsync<ArgumentException>(() => validationClient.VerificationAttempts("+59170000000", 0, CancellationToken.None), "limit zero");
    await ThrowsAsync<ArgumentException>(() => validationClient.VerificationAttempts("+59170000000", 4, CancellationToken.None), "limit four");
    await ThrowsAsync<ArgumentException>(() => validationClient.VerificationAttempts("+59170000000", 11, CancellationToken.None), "limit eleven");
    await ThrowsAsync<ArgumentException>(() => validationClient.VerificationAttempts("+591٧٠٠٠٠٠٠٠", 3, CancellationToken.None), "Unicode digits are not E.164");
    Equal(0, validationHandler.RequestCount, "validation avoids Twilio call");

    foreach (var allowedLimit in new[] { 3, 5, 10 })
    {
        var (allowedClient, allowedHandler) = AttemptClient("{\"attempts\":[]}");
        await allowedClient.VerificationAttempts("+59170000000", allowedLimit, CancellationToken.None);
        Equal(1, allowedHandler.RequestCount, $"allowed limit {allowedLimit}");
    }

    var (errorClient, _) = AttemptClient("{\"code\":60200,\"message\":\"Destino inválido +59170000000\",\"more_info\":\"https://www.twilio.com/docs/errors/60200\",\"status\":400}", HttpStatusCode.BadRequest);
    try
    {
        await errorClient.VerificationAttempts("+59170000000", 3, CancellationToken.None);
        throw new InvalidOperationException("FAIL Twilio error redaction: expected TwilioException.");
    }
    catch (TwilioException ex)
    {
        Equal(400, ex.Status, "Twilio error status");
        DoesNotContain("+59170000000", ex.Message, "Twilio error redaction");
        Contains("+591******00", ex.Message, "Twilio error masked phone");
    }

    Console.WriteLine("PASS: consulta de intentos Twilio verificada.");
}
finally
{
    Environment.SetEnvironmentVariable("TWILIO_ACCOUNT_SID", null);
    Environment.SetEnvironmentVariable("TWILIO_AUTH_TOKEN", null);
    Environment.SetEnvironmentVariable("TWILIO_VERIFY_SERVICE_SID", null);
}

var auditDirectory = Path.Combine(Path.GetTempPath(), "OtpSupport.Api.Tests", Guid.NewGuid().ToString("N"));
var auditDatabase = Path.Combine(auditDirectory, "audit.db");
Directory.CreateDirectory(auditDirectory);
try
{
    var auditConfiguration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Support:DatabasePath"] = auditDatabase })
        .Build();
    var store = new Store(auditConfiguration);
    store.Initialize();
    store.AuditAttemptLookup("+59170000000", 5, 2);

    var audit = store.History(1).Single();
    Equal("verification.attempts.lookup", audit.Action, "attempt audit action");
    Equal("+591******00", audit.PhoneMasked, "attempt audit masked phone");
    Equal("Límite solicitado: 5; resultados: 2.", audit.Detail, "attempt audit counts");
    var persistedAudit = string.Join("|", audit.Operator, audit.Action, audit.CaseId, audit.PhoneMasked, audit.Outcome, audit.Detail);
    if (persistedAudit.Contains("+59170000000", StringComparison.Ordinal))
        throw new InvalidOperationException("FAIL attempt audit privacy: full phone was persisted.");

    Console.WriteLine("PASS: privacidad de auditoría verificada.");
}
finally
{
    SqliteConnection.ClearAllPools();
    if (File.Exists(auditDatabase)) File.Delete(auditDatabase);
    if (Directory.Exists(auditDirectory)) Directory.Delete(auditDirectory);
}

sealed class RecordingHandler(string json, HttpStatusCode statusCode = HttpStatusCode.OK) : HttpMessageHandler
{
    public int RequestCount { get; private set; }
    public string? LastRequestUri { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestCount++;
        LastRequestUri = request.RequestUri?.OriginalString;
        return Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
    }
}

sealed class CapturingLoggerProvider(List<string> messages) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) => new CapturingLogger(messages);
    public void Dispose() { }
}

sealed class CapturingLogger(List<string> messages) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;
    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => messages.Add(formatter(state, exception));
}
