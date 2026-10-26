using OtpSupport.Api.Models;
namespace OtpSupport.Api.Services;
public static class DiagnosisService
{
    public static Diagnosis ForMessage(TwilioMessage m) => m.Status.ToLowerInvariant() switch
    {
        "delivered"=>new("delivery-confirmed","success","Entrega confirmada por el proveedor.","Solicite al cliente ingresar el código. La entrega no equivale a verificación aprobada."),
        "read"=>new("read-confirmed","success","Lectura confirmada.","Revise por separado el estado de Verify."),
        "sent"=>new("delivery-unknown","warning","Mensaje enviado al canal, sin confirmación de entrega.","Revisar estado y eventos de Twilio. No asumir que el cliente recibió el código."),
        "failed" or "undelivered"=>new("delivery-failed","error",$"Entrega fallida. Código Twilio: {m.ErrorCode??"sin código"}.","Revisar causa documentada antes de solicitar otro intento."),
        _=>new("pending","info",$"Estado actual: {m.Status}.","Actualizar consulta y revisar logs del proveedor.")
    };

    public static Diagnosis ForAttempt(TwilioVerificationAttempt attempt)
    {
        var conversion = attempt.ConversionStatus?.ToLowerInvariant() ?? "";
        var delivery = attempt.MessageStatus?.ToLowerInvariant() ?? "";

        if (conversion == "converted")
            return new("verification-approved", "success", "La verificación fue confirmada correctamente.", "No solicite un nuevo OTP para esta verificación.");

        if (delivery is "failed" or "undelivered")
            return new("delivery-failed", "error", $"La entrega falló. Código Twilio: {attempt.ErrorCode ?? "sin código"}.", "Revise la causa documentada antes de solicitar otro intento.");

        if (delivery is "delivered" or "read")
            return new("delivery-confirmed", "success", "La entrega fue confirmada; no equivale a una verificación aprobada.", "Confirme por separado si el cliente completó la verificación.");

        return new("delivery-pending", "warning", $"El intento continúa sin confirmación final de entrega ({attempt.MessageStatus}).", "Actualice la consulta antes de decidir un reenvío.");
    }
}
