# Hotel Reservas · versión web

## Equipo

| Integrante | GitHub | Responsabilidad en el proyecto |
|---|---|---|
| Gustavo Adolfo Cardona Bayona | [gustavo8015](https://github.com/gustavo8015) | Arquitectura, API REST, base de datos SQL Server y servicios de IA |
| Jonathan Soler Guevara | [jonsolerg-spec](https://github.com/jonsolerg-spec) | Pruebas de la app (Kiwi TCMS) y documentación |

Sistema de reservas hoteleras en línea, desarrollado para la asignatura Sistemas
Distribuidos de la Universidad Manuela Beltrán. Consta de una interfaz de usuario
en Blazor WebAssembly, un servicio REST en ASP.NET Core 8 y una base de datos en
SQL Server.

Este repositorio contiene únicamente la versión web. La aplicación móvil del
proyecto se mantiene aparte.

## Qué hace

Un visitante puede consultar el catálogo de habitaciones con fotografías y
comodidades, verificar disponibilidad por fechas y número de personas, reservar
sin necesidad de crear una cuenta, pagar con tarjeta mediante una pasarela
simulada o apartar la habitación para pagar en efectivo en recepción, y recibir
el comprobante por correo o por WhatsApp.

Quien tiene cuenta consulta además sus propias reservas y puede cancelarlas en
línea cuando la política lo permite.

El personal del hotel administra tarifas, habitaciones, huéspedes y reservas,
registra pagos en efectivo y consulta reportes de ocupación.

Las dos clases de usuario cuentan con un asistente de consultas en lenguaje
natural, de solo lectura, con un catálogo de funciones distinto según el perfil.

## Arquitectura

La solución tiene dos proyectos. `HotelReservas.Api` expone el servicio REST,
concentra las reglas de negocio y es el único que habla con la base de datos.
`HotelReservas.Web` es la interfaz en Blazor WebAssembly, que se ejecuta en el
navegador y consume la API mediante HTTP y JSON.

La autenticación se resuelve con JSON Web Tokens firmados por la API, y la
autorización por perfil se evalúa en el servidor, nunca en el navegador.

La confirmación de una reserva se hace dentro de una transacción en SQL Server
con nivel de aislamiento SERIALIZABLE y bloqueos de actualización, de modo que
dos personas no puedan tomar la misma habitación en las mismas fechas. Cuando el
cobro se aprueba pero la confirmación falla, la API revierte el cobro en la
pasarela simulada, que es una compensación del patrón saga.

## Requisitos

Se necesita el SDK de .NET 8, una instancia de SQL Server, que puede ser Express
o LocalDB, y SQL Server Management Studio para ejecutar los guiones.

## Instalación

Primero, la base de datos. Abra SQL Server Management Studio y ejecute los
guiones de la carpeta `database` en orden numérico, empezando por
`01_crear_base_de_datos.sql`. Cada guion es idempotente, de modo que se puede
ejecutar más de una vez sin dañar los datos. Los guiones están guardados con
marca de orden de bytes UTF-8 para que las tildes no se dañen al abrirlos.

Segundo, la cadena de conexión. Revise `HotelReservas.Api/appsettings.json` y
ajústela a su instancia. Si va a publicar el servicio, reemplace también el valor
de `Jwt:Key`, que viene con un texto de ejemplo.

Tercero, los dos proyectos. En una consola, dentro de `HotelReservas.Api`,
ejecute `dotnet run`. En otra consola, dentro de `HotelReservas.Web`, ejecute
`dotnet run`. La API verifica el esquema al arrancar y escribe en la consola el
nombre del guion que falte, si alguno no se ejecutó.

## Usuarios de prueba

El guion `03_datos_de_prueba.sql` crea los usuarios de demostración y el guion
`06_consulta_asistente.sql` establece sus contraseñas. El administrador entra con
el correo `admin@hotel.com`. La contraseña de todos los usuarios de prueba es
`Hotel2026*` y debe cambiarse antes de cualquier uso real.

## Tolerancia a fallos

El cliente web aplica tres mecanismos sobre cada petición, implementados en
`HotelReservas.Web/Services/Resiliencia.cs`.

Un tiempo de espera de ocho segundos por petición, para que una conexión colgada
no deje la pantalla esperando de forma indefinida.

Reintentos con retirada exponencial y una fracción aleatoria, de modo que varios
clientes que fallaron a la vez no vuelvan a golpear el servicio en el mismo
instante. Se aplican únicamente a operaciones idempotentes: un POST que crea una
reserva nunca se reintenta de forma automática, porque si lo que se perdió fue la
respuesta y no la petición, el reintento crearía una segunda reserva.

Un interruptor de circuito que, tras cuatro fallos consecutivos, se abre durante
quince segundos y falla de inmediato en lugar de hacer esperar al usuario. Pasado
ese periodo deja pasar una petición de prueba. La aplicación anuncia el estado en
pantalla en lugar de dejar que cada página falle por su cuenta.

Del lado del servicio, las llamadas al proveedor del modelo del asistente llevan
reintentos y, si se agotan, el orquestador cae a un intérprete de reglas local.

## Verificación de salud

`GET /health` responde mientras el proceso esté vivo y no consulta nada externo:
es la señal que usa un orquestador para decidir si hay que reiniciar el servicio.

`GET /health/ready` ejecuta además una consulta real contra SQL Server con un
tope de tres segundos: es la señal para que un balanceador deje de enviar
peticiones a una instancia cuya base de datos no responde.

## Despliegue en contenedores

```bash
cp .env.ejemplo .env     # y cambie SQL_PASSWORD y JWT_KEY
docker compose up -d --build
```

La composición levanta la base de datos, un trabajo que carga los guiones de
`database` en orden y termina, el servicio, y una pasarela nginx que sirve el
cliente y reenvía `/api`. El portal queda en `http://localhost:8090` y el
servicio en `http://localhost:8080`. Los datos se conservan en un volumen con
nombre, de modo que detener y volver a levantar no borra las reservas.

## Pruebas

El proyecto `pruebas` es un banco de pruebas de consola que no depende de ninguna
biblioteca externa.

```bash
dotnet run --project pruebas -- --api http://localhost:5080
```

Ejecuta cinco suites: funcionales, de integración, de concurrencia sobre la misma
habitación, de rendimiento con percentiles y de tolerancia a fallos. Deja los
resultados en `evidencias.md` y `evidencias.json`, y devuelve código de salida 0
si todas pasan.

La prueba de concurrencia verifica el requisito central del sistema: lanza varias
peticiones de reserva sobre la misma habitación y las mismas fechas, soltadas a
la vez con una barrera, y espera que exactamente una quede confirmada mientras
las demás reciben el error 50001.

## El asistente

El asistente responde consultas en lenguaje natural y es de solo lectura: no
crea, no modifica, no cancela reservas ni registra pagos.

Funciona con cualquier proveedor de modelo que exponga la interfaz compatible con
OpenAI, es decir un POST a `{BaseUrl}/chat/completions` que admita el parámetro
`tools`. La configuración está en la sección `Asistente` de `appsettings.json` y
cambiar de proveedor no requiere tocar código.

Mientras no haya una clave configurada, responde un intérprete basado en reglas
que no necesita internet ni cuenta. Entiende menos variantes de la misma
pregunta, pero consulta exactamente los mismos datos y devuelve las mismas
cifras.

El catálogo de funciones se recorta según el rol del token antes de mostrárselo
al modelo, y la función devuelta se valida de nuevo contra ese catálogo antes de
ejecutarla. El huésped nunca puede obtener reservas ajenas ni cifras de operación
del hotel.

## Protección de datos personales

El asistente pide consentimiento antes de la primera consulta, conforme al
artículo 9 de la Ley 1581 de 2012, y antes de enviar la consulta al proveedor del
modelo borra documentos de identidad, teléfonos y correos, que es el principio de
minimización del artículo 4 literal f de la misma ley. Toda consulta queda
registrada en la tabla `CONSULTA_ASISTENTE`.

De las tarjetas de pago solo se conservan la franquicia y los cuatro últimos
dígitos. El número completo, el titular y el código de seguridad no se almacenan
en ninguna tabla.

## Organización del repositorio

`HotelReservas.Api` contiene el servicio REST, con los controladores, el contexto
de Entity Framework Core, los servicios de negocio y el asistente.

`HotelReservas.Web` contiene la interfaz en Blazor WebAssembly, con las páginas,
el cliente de la API, la hoja de estilos y las imágenes del catálogo.

`database` contiene los guiones de creación y evolución del esquema, numerados en
el orden en que deben ejecutarse.

`design-system` contiene las decisiones de interfaz y la paleta institucional.

`pruebas` contiene el banco de pruebas y `despliegue`, la configuración de la
pasarela nginx. En `docs` está el manual de instalación y ejecución.

`HotelReservas.Web.Html` contiene el prototipo estático inicial de la interfaz,
que se conserva como antecedente del diseño.

## Licencia y uso

Trabajo académico de la Universidad Manuela Beltrán. Las fotografías de las
habitaciones y la ilustración del asistente son recursos propios del proyecto.
