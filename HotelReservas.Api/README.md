# HotelReservas.Api

API REST del sistema Hotel Reservas (ASP.NET Core 8, Entity Framework Core, SQL Server). Es la **capa de lógica de negocio y de acceso a datos** de la arquitectura en capas de la sección 4 del documento maestro, e incluye el **servicio de inteligencia artificial** de la sección 13: el asistente de consultas en lenguaje natural con Azure OpenAI y llamada a funciones de solo lectura.

## Cómo ejecutar

1. Base de datos: en SSMS ejecutar, en orden, `database/01`, `02`, `03`, `05` y `06` (el 06 crea `CONSULTA_ASISTENTE` y asigna la contraseña de prueba `Hotel2026*` a los tres usuarios).
2. Instalar el SDK de .NET 8 (viene con Visual Studio 2022 17.8+).
3. Revisar `appsettings.json`:
   - `ConnectionStrings:HotelReservas` (por defecto `localhost` con autenticación de Windows; para `.\SQLEXPRESS` cambie el servidor).
   - `Jwt:Key` (cualquier texto de 32 caracteres o más).
   - `AzureOpenAI` (opcional, ver abajo).
4. En la carpeta `HotelReservas.Api`: `dotnet run` (o F5 en Visual Studio). Se abre Swagger en `http://localhost:5080/swagger`.
5. Probar con el archivo `HotelReservas.Api.http`: login → copiar el token → consultas.

Para agregarla a la solución: `dotnet sln HotelReservas.sln add HotelReservas.Api/HotelReservas.Api.csproj`.

## Aplicación web

`wwwroot/index.html` es la versión web del sistema (misma funcionalidad que la APK, con interfaz de escritorio). Al ejecutar la API queda disponible en `http://localhost:5080/`: en la pantalla de inicio elija **Conectar a la API** (URL `http://localhost:5080`, usuario `recepcion1@hotel.com`, contraseña `Hotel2026*`) para operar sobre SQL Server, o **Demostración** para usarla sin base de datos. El archivo también funciona abierto directamente en el navegador en modo demostración.

## Recursos

| Recurso | Método | Caso de uso / requerimiento |
|---|---|---|
| `/api/auth/login` | POST | CU-01, RF01, RNF01: token JWT con el perfil del usuario |
| `/api/disponibilidad?tipo=&entrada=&salida=` | GET | CU-03, RF03: habitaciones libres, reservadas y en mantenimiento, con subtotal, IVA 19 % y precio neto |
| `/api/reservas` | POST | CU-04, CU-05, CU-06, RF16: ejecuta `sp_ConfirmarReserva` (SERIALIZABLE + UPDLOCK/HOLDLOCK); **201** con código y comprobante o **409** si la habitación ya no está disponible (error 50001) |
| `/api/reservas`, `/api/reservas/{codigo}` | GET | CU-12, RNF05: historial y detalle con pagos, comprobante y bitácora |
| `/api/reservas/{codigo}/checkin`, `/checkout`, `/cancelar` | PUT | CU-08, CU-09, CU-10: cambio de estado con concurrencia optimista (`versionFila` = ROWVERSION); **409** si otra sesión modificó la reserva |
| `/api/huespedes?documento=` | GET | RF18: huésped frecuente (≥ 2 estadías) y preferencias de la última estadía |
| `/api/huespedes` | POST | CU-02, RF02: registro de huésped; **409** si el documento ya existe |
| `/api/comprobantes/{numero}/compartir` | GET | RF17: texto del comprobante y enlace `wa.me` al teléfono del huésped |
| `/api/asistente` | POST | **Sección 13**: consulta en lenguaje natural → función de lectura → respuesta |
| `/api/asistente/funciones`, `/api/asistente/estado` | GET | Catálogo entregado al modelo e intérprete activo |

## El servicio de IA (sección 13 del documento)

Los cinco componentes de la sección 13.3 corresponden a estas piezas del código:

| Componente | Dónde está | Qué hace |
|---|---|---|
| Modelo de datos (13.3.1) | `database/06_consulta_asistente.sql`, `Data/Entidades.cs` | Tabla `CONSULTA_ASISTENTE` (usuario, fecha, texto filtrado, función, parámetros, intérprete, éxito, duración). El asistente no modifica las nueve tablas del núcleo. |
| Modelo de IA (13.3.2) | `Asistente/InterpreteAzureOpenAI.cs` | GPT-4o vía Azure OpenAI Service (API REST de chat completions, `tools` + `tool_choice`), temperatura 0, mensaje de sistema fijo que lo limita a responder con los resultados de las funciones y a remitir las escrituras a la pantalla Reservas. Dos llamadas: interpretar (elige función y parámetros) y redactar (a partir del resultado real). |
| Lógica de integración (13.3.3) | `Asistente/OrquestadorAsistente.cs`, `Asistente/CatalogoFunciones.cs`, `Controllers/AsistenteController.cs` | Controlador `/api/asistente` con JWT → filtro de datos personales (documentos, teléfonos y correos se reemplazan antes de salir al servicio externo) → intérprete → validación contra el catálogo de solo lectura (`consultarDisponibilidad`, `consultarTarifa`, `consultarReserva`, `consultarLlegadas`, `consultarReservasConfirmadas`) → ejecución con los mismos servicios de CU-03 y CU-12 → registro en `CONSULTA_ASISTENTE` → respuesta con texto y datos estructurados. |
| Interfaz de usuario (13.3.4) | Pantalla Asistente de la app (`HotelReservas/Resources/Raw/prototipo.html`) | Envía `{ "consulta": "..." }` y muestra la respuesta junto con la función consultada (origen de la información). |
| Infraestructura (13.3.5) | `appsettings.json` → `AzureOpenAI` | Punto de conexión, clave y despliegue se guardan en la configuración segura de la API (o en variables de entorno `AzureOpenAI__ApiKey`), nunca en la aplicación móvil. HTTPS entre API y servicio. |

### Con y sin Azure OpenAI

- **Con credenciales** (`AzureOpenAI:Endpoint` como `https://<recurso>.openai.azure.com`, `ApiKey` y `Deployment` de un modelo GPT-4o): el intérprete es `AZURE_OPENAI`.
- **Sin credenciales o si el servicio no responde**: la API cambia automáticamente al intérprete `REGLAS` (`Asistente/InterpreteReglas.cs`), que reconoce las mismas consultas con expresiones regulares y ejecuta el mismo catálogo. Así la demostración funciona sin internet y el comportamiento es idéntico desde la app; `GET /api/asistente/estado` indica cuál está activo y cada fila de `CONSULTA_ASISTENTE` registra con cuál se atendió.

### Reglas de diseño que el código hace cumplir

1. El asistente solo ejecuta funciones del catálogo de lectura; cualquier otro nombre devuelto por el modelo se rechaza (`OrquestadorAsistente`).
2. La confirmación de reservas (CU-04) se hace únicamente por `POST /api/reservas`, con la verificación y el bloqueo del procedimiento almacenado; el asistente no tiene acceso a ese recurso.
3. Al servicio externo solo viaja la pregunta filtrada y el resumen del resultado; nunca documentos, teléfonos ni correos de los huéspedes (Ley 1581 de 2012).
