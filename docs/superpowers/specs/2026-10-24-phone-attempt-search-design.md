# Búsqueda de intentos OTP por teléfono

## Intención

Permitir que un operador busque los intentos recientes de OTP asociados a un número telefónico sin conocer previamente un Message SID o Verification SID. El operador podrá solicitar los últimos 3, 5 o 10 intentos y abrir un diagnóstico detallado de cada resultado.

## Alcance

- Agregar «Número telefónico» como tipo de consulta en la pantalla existente de Consulta OTP.
- Aceptar únicamente números en formato E.164.
- Agregar un selector limitado a 3, 5 o 10 intentos.
- Consultar Twilio Verification Attempts filtrando por número, el Verify Service configurado y el canal WhatsApp.
- Ordenar los resultados del más reciente al más antiguo.
- Mostrar en la lista fecha, estado de entrega, estado de conversión, Verification SID y Attempt SID.
- Al seleccionar un resultado, mostrar el detalle disponible y un diagnóstico operativo.
- Registrar la consulta en la auditoría local usando solamente el teléfono enmascarado.

## Fuera de alcance

- Buscar o revelar el código OTP.
- Consultar canales distintos de WhatsApp.
- Conservar localmente una copia completa de los intentos de Twilio.
- Recuperar intentos fuera de la ventana histórica ofrecida por Twilio.
- Modificar el flujo actual de reenvío.

## Restricciones y seguridad

- Verification Attempts solo permite consultar intentos de los últimos 30 días y actualmente es una API Public Beta.
- El backend validará el teléfono y el límite; solamente admitirá 3, 5 o 10.
- El teléfono completo viajará únicamente entre el navegador local, el backend local y Twilio. Las respuestas de la aplicación y la auditoría lo mostrarán enmascarado.
- Se reutilizarán `TWILIO_ACCOUNT_SID`, `TWILIO_AUTH_TOKEN` y `TWILIO_VERIFY_SERVICE_SID`; no se agregarán secretos al frontend.
- Las llamadas seguirán protegidas por la sesión local, CORS y el limitador existentes.

## Diseño

### Backend

Se agregará `GET /api/verifications/attempts?phone={E.164}&limit={3|5|10}`. El endpoint validará los parámetros y llamará a un nuevo método de `TwilioClient` que consulte `GET https://verify.twilio.com/v2/Attempts` con los filtros de destino, servicio Verify, canal `whatsapp` y tamaño solicitado.

El cliente transformará la respuesta de Twilio a un contrato propio con los siguientes campos: Attempt SID, Verification SID, fecha de creación y actualización, canal, estado de conversión, estado del mensaje, estado del intento, código de error y teléfono enmascarado. El backend ordenará por fecha descendente y devolverá como máximo la cantidad solicitada.

`DiagnosisService` generará un diagnóstico por intento combinando el estado de entrega, el estado de conversión y el código de error. Los fallos o no entregados tendrán severidad de error; los estados todavía en tránsito tendrán advertencia; una entrega confirmada o conversión confirmada se distinguirá explícitamente para no confundir entrega con aprobación.

Si Twilio no devuelve coincidencias, la respuesta será una lista vacía, no un error. Los errores de autenticación, límite o conectividad usarán el manejo existente.

### Frontend

El selector actual de tipo de consulta incorporará «Número telefónico». Para ese tipo se mostrará un campo E.164 y un combo «Últimos intentos» con 3, 5 y 10; el valor inicial será 3.

La zona de resultados presentará una lista ordenada. Cada fila permitirá seleccionar un intento y mostrará un panel de detalle con sus identificadores, fechas, estados, error y recomendación. Una lista vacía mostrará un mensaje específico indicando la ventana máxima de 30 días.

Las consultas existentes por Message SID y Verification SID conservarán su comportamiento.

## Alternativas consideradas

1. **Verification Attempts de Twilio — seleccionada.** Recupera intentos históricos ya existentes y permite filtrar por destino, servicio y WhatsApp. Se acepta su ventana de 30 días y condición Public Beta.
2. **Twilio Messaging por destinatario.** Fue descartada porque no ofrece de forma directa la conversión ni toda la relación con Verify y exigiría correlación adicional.
3. **Índice local propio.** Fue descartado porque solo tendría información generada después de instalar el cambio y no cubriría el historial ya disponible en Twilio.

## Verificación

- Probar validación de teléfono y de los límites permitidos.
- Probar el mapeo de una respuesta simulada de Twilio, el orden descendente y el máximo solicitado.
- Probar diagnósticos de entrega exitosa, pendiente y fallida.
- Comprobar que la auditoría no persista el número completo.
- Ejecutar compilación y pruebas del backend y compilación del frontend.
- Verificar manualmente los estados de lista vacía, listado y detalle seleccionado.

