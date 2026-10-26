using Microsoft.Extensions.Logging;

namespace OtpSupport.Api.Services;

public static class SensitiveLogging
{
    public static void Configure(ILoggingBuilder logging)
    {
        logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.None);
        logging.AddFilter("System.Net.Http.HttpClient", LogLevel.None);
    }
}
