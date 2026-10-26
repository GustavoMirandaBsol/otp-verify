# Diseño de carga local de variables de entorno

## Intención

Permitir que el backend y el frontend arranquen localmente con configuración guardada en archivos ignorados por Git, sin enviar secretos de Twilio al navegador.

## Objetivos

- Cargar las credenciales y la contraseña del backend desde `backend/.env.local` al ejecutar `launch-backend.ps1`.
- Configurar el destino local del proxy de Vite mediante `frontend/.env.local`.
- Entregar plantillas `.env.example` seguras y documentar el flujo de arranque.
- Mantener prioridad para las variables que ya estén definidas en la terminal.

## Fuera de alcance

- Incluir credenciales reales de Twilio en el proyecto.
- Configurar ambientes de despliegue o almacenamiento institucional de secretos.
- Cambiar los puertos locales, el mecanismo de autenticación o las APIs existentes.

## Restricciones y seguridad

- El flujo debe funcionar en Windows PowerShell con .NET 8 y Vite 6.
- `TWILIO_AUTH_TOKEN` y `OTP_SUPPORT_PASSWORD` nunca deben estar disponibles mediante variables `VITE_*` ni incorporarse al bundle del frontend.
- Los archivos `.env.local` deben permanecer ignorados por Git; solo las plantillas `.env.example` son compartibles.
- La contraseña local debe tener al menos 14 caracteres, como ya exige el backend.

## Enfoques considerados

1. **Archivos separados por aplicación (seleccionado):** mantiene secretos en el backend y deja en el frontend únicamente el destino del proxy. Requiere dos archivos, pero conserva una frontera de seguridad clara.
2. **Un archivo único en la raíz:** reduce el número de archivos, pero mezcla configuración pública y secreta y acopla la carga de ambas aplicaciones.

## Comportamiento acordado

- `backend/.env.local` define `TWILIO_ACCOUNT_SID`, `TWILIO_AUTH_TOKEN`, `TWILIO_VERIFY_SERVICE_SID` y `OTP_SUPPORT_PASSWORD`.
- `launch-backend.ps1` lee asignaciones `NOMBRE=valor`, omite líneas vacías y comentarios, y no reemplaza variables ya presentes en la sesión.
- `frontend/.env.local` define `VITE_API_PROXY_TARGET`, con `http://127.0.0.1:5088` como valor local.
- `vite.config.ts` usa esa variable para el proxy `/api` y conserva el mismo destino como fallback.
- El README dirige el arranque mediante los dos scripts de la raíz.

