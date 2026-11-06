# Hotel Reservas · versión web

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

`HotelReservas.Web.Html` contiene el prototipo estático inicial de la interfaz,
que se conserva como antecedente del diseño.

## Licencia y uso

Trabajo académico de la Universidad Manuela Beltrán. Las fotografías de las
habitaciones y la ilustración del asistente son recursos propios del proyecto.
