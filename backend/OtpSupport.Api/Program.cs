using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.RateLimiting;
using OtpSupport.Api.Data;
using OtpSupport.Api.Models;
using OtpSupport.Api.Services;
using System.Threading.RateLimiting;
var builder=WebApplication.CreateBuilder(args);
SensitiveLogging.Configure(builder.Logging);
builder.WebHost.UseUrls("http://127.0.0.1:5088");
builder.Services.AddSingleton<Store>();
builder.Services.AddHttpClient<TwilioClient>(c=>c.Timeout=TimeSpan.FromSeconds(20));
builder.Services.AddRateLimiter(o=>o.AddFixedWindowLimiter("api",x=>{x.PermitLimit=60;x.Window=TimeSpan.FromMinutes(1);x.QueueLimit=0;}));
builder.Services.AddCors(o=>o.AddPolicy("local",p=>p.WithOrigins("http://localhost:5173","http://127.0.0.1:5173").AllowAnyHeader().AllowAnyMethod()));
var app=builder.Build();app.Services.GetRequiredService<Store>().Initialize();app.UseCors("local");app.UseRateLimiter();
// This local app uses a session token generated on startup. Password is never stored in source.
var password=Environment.GetEnvironmentVariable("OTP_SUPPORT_PASSWORD");
if(string.IsNullOrWhiteSpace(password)||password.Length<14)throw new InvalidOperationException("Set OTP_SUPPORT_PASSWORD (14+ characters) before starting.");
var sessionToken=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var loginAttempts=new Queue<DateTimeOffset>();
app.MapPost("/api/auth/login",(LoginRequest req,HttpContext ctx)=>{
    lock(loginAttempts){while(loginAttempts.Count>0&&loginAttempts.Peek()<DateTimeOffset.UtcNow.AddMinutes(-10))loginAttempts.Dequeue();if(loginAttempts.Count>=10)return Results.StatusCode(429);loginAttempts.Enqueue(DateTimeOffset.UtcNow);}
    var a=SHA256.HashData(Encoding.UTF8.GetBytes(req.Password??""));var b=SHA256.HashData(Encoding.UTF8.GetBytes(password));
    if(!CryptographicOperations.FixedTimeEquals(a,b))return Results.Unauthorized();
    lock(loginAttempts)loginAttempts.Clear();return Results.Ok(new{token=sessionToken,operatorName="LocalSupport"});
}).RequireRateLimiting("api");
var api=app.MapGroup("/api").RequireRateLimiting("api").AddEndpointFilter(async (ctx,next)=>{
    var bearer=ctx.HttpContext.Request.Headers.Authorization.ToString();
    var expected="Bearer "+sessionToken;
    if(!CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(bearer)),SHA256.HashData(Encoding.UTF8.GetBytes(expected))))return Results.Unauthorized();
    if(ctx.HttpContext.Request.Headers.Origin is var origin && !string.IsNullOrEmpty(origin) && origin!="http://localhost:5173" && origin!="http://127.0.0.1:5173")return Results.StatusCode(403);
    return await next(ctx);
});
api.MapGet("/config",(TwilioClient twilio)=>Results.Ok(new{configured=twilio.IsConfigured}));
api.MapGet("/messages/{sid}",async (string sid,TwilioClient twilio,Store store,CancellationToken ct)=>{
    try {var msg=await twilio.Message(sid,ct);store.Audit("LocalSupport","message.lookup","",msg.To??"","ok",$"SID {sid}; status {msg.Status}");return Results.Ok(new{message=msg,diagnosis=DiagnosisService.ForMessage(msg)});}
    catch(Exception ex){return Handle(ex);}
});
api.MapGet("/verifications/attempts",async (string phone,int? limit,TwilioClient twilio,Store store,CancellationToken ct)=>{
    try{var normalizedLimit=limit??3;var attempts=await twilio.VerificationAttempts(phone,limit,ct);store.AuditAttemptLookup(phone,normalizedLimit,attempts.Count);return Results.Ok(attempts.Select(attempt=>new VerificationAttemptSearchItem(attempt,DiagnosisService.ForAttempt(attempt))));}
    catch(Exception ex){return Handle(ex);}
});
api.MapGet("/verifications/{sid}",async (string sid,TwilioClient twilio,Store store,CancellationToken ct)=>{
    try{var v=await twilio.Verification(sid,ct);store.Audit("LocalSupport","verification.lookup","",v.To??"","ok",$"SID {sid}; status {v.Status}");return Results.Ok(v);}
    catch(Exception ex){return Handle(ex);}
});
api.MapGet("/history",(Store store,int? limit)=>Results.Ok(store.History(limit??50)));
api.MapPost("/verifications/resend",async (ResendRequest req,TwilioClient twilio,Store store,IConfiguration cfg,CancellationToken ct)=>{
    var phone=req.Phone?.Trim()??"";var masked=TwilioClient.MaskPhone(phone);
    if(!Regex.IsMatch(phone,@"^\+[1-9]\d{7,14}$")||!Regex.IsMatch(req.CaseId??"",@"^[a-zA-Z0-9_-]{6,80}$")||string.IsNullOrWhiteSpace(req.Reason)||req.Reason.Length<10||req.Reason.Length>300||!req.CustomerRequested||!req.IdentityVerified)
        return Results.BadRequest(new ApiError("Datos inválidos: teléfono E.164, caso, motivo y confirmaciones obligatorios."));
    // Optional SID must be checked against the target number before initiating a new verification.
    if(!string.IsNullOrWhiteSpace(req.VerificationSid))
    {
        try{var prior=await twilio.Verification(req.VerificationSid,ct);if(prior.To!=masked||prior.Channel!="whatsapp")return Results.BadRequest(new ApiError("La verificación no coincide con el teléfono/canal."));if(prior.Status=="approved")return Results.Conflict(new ApiError("La verificación ya fue aprobada."));}
        catch(Exception ex){return Handle(ex);}
    }
    var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(phone)));
    if(!store.TryReserve(hash,req.CaseId,cfg.GetValue("Support:MinimumResendIntervalSeconds",60),cfg.GetValue("Support:MaxResendsPerHour",3),cfg.GetValue("Support:MaxResendsPerDay",6),out var reason))
    {store.Audit("LocalSupport","otp.resend",req.CaseId,masked,"blocked",reason);return Results.Json(new ApiError(reason),statusCode:429);}
    try{var result=await twilio.SendWhatsApp(phone,ct);store.Audit("LocalSupport","otp.resend",req.CaseId,masked,"requested",$"Reason: {req.Reason}; Verification: {result.Sid}; status: {result.Status}");return Results.Ok(result);}
    catch(Exception ex){store.Audit("LocalSupport","otp.resend",req.CaseId,masked,"failed",ex is TwilioException te?$"Twilio HTTP {te.Status}: {te.Message}":"Network/other failure");return Handle(ex);}
});
app.Run();
static IResult Handle(Exception ex)=>ex switch{
    ArgumentException a=>Results.BadRequest(new ApiError(a.Message)),
    TwilioException t=>Results.Json(new ApiError("Error de Twilio",t.Message),statusCode:t.Status is >=400 and <=599?t.Status:502),
    HttpRequestException=>Results.Json(new ApiError("No se pudo conectar con Twilio."),statusCode:502),
    TaskCanceledException=>Results.Json(new ApiError("Tiempo de espera agotado."),statusCode:504),
    _=>Results.Json(new ApiError("Error interno de consulta."),statusCode:500)
};
