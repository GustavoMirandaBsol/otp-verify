# OTP Support Console

Herramienta local de soporte para OTP por WhatsApp, con React + TypeScript + Vite + componentes shadcn/ui (Button y Card, implementados siguiendo su patrón) y ASP.NET Core 8.

## Funciones
- Consultar un Message SID `SM...` mediante Twilio Messages REST API.
- Consultar un Verification SID `VE...` mediante Twilio Verify REST API.
- Buscar los últimos 3, 5 o 10 intentos de Verify por número E.164 y abrir su diagnóstico detallado.
- Interpretar estados de entrega sin confundirlos con validación de OTP.
- Solicitar una nueva verificación por WhatsApp (sin SMS), con consentimiento declarado, motivo, caso y límites.
- Guardar historial de acciones en SQLite local; enmascarar teléfonos en respuestas y auditoría.

**Limitación:** la búsqueda por número usa Twilio Verification Attempts, una API Public Beta que consulta como máximo los últimos 30 días. Los resultados se restringen al Verify Service configurado y al canal WhatsApp. La herramienta tampoco garantiza la entrega ni verifica automáticamente la identidad del cliente.

## Requisitos
- Windows 10/11, .NET SDK 8, Node.js 20.19+ o 22.12+, npm.
- Cuenta Twilio con Verify Service habilitado para WhatsApp y remitente configurado.
- Conectividad saliente HTTPS a `api.twilio.com` y `verify.twilio.com`.

## Arranque en VS Code (PowerShell)

1. Descomprimir ZIP y abrir la carpeta `otp-support-console` en VS Code.
2. Si los archivos locales todavía no existen, crearlos desde las plantillas:

```powershell
Copy-Item backend/.env.example backend/.env.local
Copy-Item frontend/.env.example frontend/.env.local
```

3. Editar `backend/.env.local` y completar las cuatro variables privadas:

```dotenv
TWILIO_ACCOUNT_SID=ACxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
TWILIO_AUTH_TOKEN=su-token-secreto
TWILIO_VERIFY_SERVICE_SID=VAxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx
OTP_SUPPORT_PASSWORD=una-contrasena-larga-de-14-o-mas
```

   `OTP_SUPPORT_PASSWORD` debe tener al menos 14 caracteres. Una variable definida previamente en la terminal tiene prioridad sobre el archivo local.

4. El frontend usa únicamente una dirección pública de desarrollo en `frontend/.env.local`:

```dotenv
VITE_API_PROXY_TARGET=http://127.0.0.1:5088
```

   No coloque credenciales de Twilio ni la contraseña en el frontend.

5. Iniciar cada aplicación en una terminal PowerShell separada desde la raíz del proyecto:

```powershell
./launch-backend.ps1
```

```powershell
./launch-frontend.ps1
```

6. Abrir `http://127.0.0.1:5173` e ingresar el valor de `OTP_SUPPORT_PASSWORD`.

El backend solo escucha en `127.0.0.1:5088` y Vite en `127.0.0.1:5173`. Vite redirige `/api` al backend. No exponer los puertos en red. No guardar credenciales en frontend, archivos `.env` compartidos ni repositorios.

## Ejemplos de endpoints locales

- `POST /api/auth/login` `{ "password": "..." }` -> bearer temporal (válido mientras vive el proceso)
- `GET /api/messages/{SM...}` -> estado y diagnóstico de entrega
- `GET /api/verifications/{VE...}` -> estado de verificación
- `GET /api/verifications/attempts?phone=%2B5917XXXXXXX&limit=3` -> últimos intentos por número; `limit` admite `3`, `5` o `10`
- `GET /api/history?limit=60` -> auditoría local
- `POST /api/verifications/resend`:

```json
{
  "caseId": "CASE-123456",
  "phone": "+5917XXXXXXX",
  "reason": "Cliente reporta OTP no recibido; solicita un nuevo intento.",
  "customerRequested": true,
  "identityVerified": true,
  "verificationSid": null
}
```

Las llamadas protegidas requieren `Authorization: Bearer <token>` obtenido mediante login. `verificationSid` es opcional, pero si se proporciona se comprueba que corresponda al número y al canal WhatsApp y que no esté aprobado.

## Consideraciones bancarias / antes de producción

**Este es un MVP local, no una solución de producción certificada.** El operador declara que verificó la identidad, pero el programa no puede demostrar que el número pertenece a un `ClientGuid`/`OfferGuid` autorizado. Para uso real con clientes, integrar la autorización corporativa (Entra ID u otro IdP), un servicio central de casos de CRM/Crédito, control de acceso por rol y validación de teléfono asociado a la solicitud. El backend local almacena credenciales en variables de entorno del proceso; esto es aceptable para pruebas controladas, pero no para distribución masiva a operadores. Preferir un backend central y secretos en Key Vault para despliegue institucional.

El registro de auditoría SQLite local no es inmutable ni centralizado; integrar SIEM/auditoría central antes de operar en producción. Los límites locales se aplican por hash del teléfono (3/hora, 6/día y 60 segundos entre solicitudes), pero no sustituyen los límites globales de Twilio Verify. La reserva de cuota se consume incluso cuando el proveedor rechaza el intento para evitar reintentos agresivos. No hay endpoint para consultar ni aprobar el código OTP: solo el flujo crediticio original debe validarlo.

Los envíos por WhatsApp pueden tener costo y estar sujetos a límites, plantillas y políticas de Meta. `pending` / `sent` **no** implican entrega. La búsqueda por número consulta Twilio únicamente desde el backend; los resultados y la auditoría muestran el teléfono enmascarado. Para obtener un historial superior a 30 días debe integrarse con la base de solicitudes crediticias.

## Seguridad del entorno
- Use equipo corporativo administrado y protegido.
- No compartir contraseñas ni Auth Token.
- Use credenciales Twilio con mínimos privilegios posibles y políticas internas.
- Proteja y cifre el disco local que contiene `backend/OtpSupport.Api/data/otp-support.db`.
- La sesión local expira al reiniciar el backend.
- Para cambiar contraseña, reinicie el backend con `OTP_SUPPORT_PASSWORD` actualizado.
