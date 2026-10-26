# Local Environment Loading Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Cargar de forma local y segura la configuración separada del backend y del frontend.

**Architecture:** El script PowerShell del backend importará un archivo dotenv privado antes de ejecutar ASP.NET Core, respetando la sesión actual. Vite cargará su archivo local nativamente y la configuración del servidor utilizará únicamente una variable pública para el destino del proxy.

**Tech Stack:** PowerShell, ASP.NET Core 8, React, TypeScript, Vite 6

**Spec:** `docs/superpowers/specs/2026-10-23-local-environment-design.md`

## Global Constraints

- No exponer `TWILIO_AUTH_TOKEN` ni `OTP_SUPPORT_PASSWORD` mediante variables `VITE_*`.
- Mantener `127.0.0.1:5088` y `127.0.0.1:5173` como valores locales predeterminados.
- Mantener los `.env.local` ignorados y permitir versionar únicamente `.env.example`.
- Las variables existentes en la sesión PowerShell tienen prioridad sobre `backend/.env.local`.
- El workspace no contiene metadatos Git disponibles, por lo que no se incluyen pasos de commit.

## Review Focus

- Una línea vacía o un comentario en `backend/.env.local` se ignora.
- Un valor que contiene `=` se conserva completo después del primer separador.
- Una variable ya definida en la terminal no es reemplazada por el archivo.
- La ausencia de una variable requerida produce el error claro que ya ofrece el lanzador.
- Ningún secreto del backend aparece en la configuración o el bundle del frontend.

---

### Task 1: Carga local separada y documentación

**Files:**
- Create: `backend/.env.local`
- Create: `backend/.env.example`
- Create: `frontend/.env.local`
- Create: `frontend/.env.example`
- Modify: `launch-backend.ps1`
- Modify: `frontend/vite.config.ts`
- Modify: `README.md`

**Interfaces:**
- Consumes: asignaciones dotenv `NOMBRE=valor` y la API `loadEnv(mode, root, prefix)` de Vite.
- Produces: variables de proceso para ASP.NET Core y `VITE_API_PROXY_TARGET` para el proxy de desarrollo.

- [x] **Step 1: Registrar una comprobación inicial que demuestre la ausencia de la carga local**

Verificar que `launch-backend.ps1` no lee `backend/.env.local` y que `vite.config.ts` contiene un destino literal. El resultado inicial esperado es que ambas comprobaciones detecten el comportamiento pendiente.

- [x] **Step 2: Crear las plantillas y los archivos locales**

Definir las cuatro claves del backend con valores de ejemplo no secretos y `VITE_API_PROXY_TARGET=http://127.0.0.1:5088` en el frontend. Confirmar con `git check-ignore` si Git estuviera disponible; en este workspace, validar las reglas existentes de `.gitignore` mediante inspección.

- [x] **Step 3: Implementar la importación dotenv en `launch-backend.ps1`**

Leer `backend/.env.local`; omitir espacios, comentarios y líneas vacías; dividir cada asignación en el primer `=`; retirar comillas exteriores coincidentes; establecer la variable solo cuando no exista en la sesión. Si el archivo falta, mostrar una instrucción clara antes de validar las cuatro claves requeridas.

- [x] **Step 4: Verificar la sintaxis y los casos de importación del script**

Ejecutar PowerShell sin perfil para validar la sintaxis. Probar con valores temporales que los comentarios y líneas vacías se omiten, los valores con `=` se conservan y una variable preexistente mantiene prioridad. Probar también la ausencia del archivo y de cada clave requerida; ambos casos deben terminar antes de ejecutar `dotnet run` y mostrar una instrucción clara. Resultado esperado: todas las aserciones terminan sin error.

- [x] **Step 5: Hacer configurable el proxy de Vite**

Cambiar `frontend/vite.config.ts` a la forma funcional de `defineConfig`, cargar el modo activo con `loadEnv`, y usar `env.VITE_API_PROXY_TARGET || 'http://127.0.0.1:5088'` como `server.proxy['/api'].target`.

- [x] **Step 6: Documentar la preparación local**

Actualizar `README.md` para indicar que se copian o editan los `.env.local`, que solo el backend recibe secretos y que el arranque recomendado es `./launch-backend.ps1` y `./launch-frontend.ps1` en terminales separadas.

- [x] **Step 7: Ejecutar la verificación integral**

Ejecutar `dotnet build backend/OtpSupport.Api/OtpSupport.Api.csproj` y `npm run build` desde `frontend`. Revisar la salida generada para confirmar que no contiene los nombres ni valores de secretos del backend.

