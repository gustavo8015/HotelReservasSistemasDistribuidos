# Manual de instalación y ejecución
## Sistema distribuido de reservas hoteleras

Universidad Manuela Beltrán · Sistemas Distribuidos · Noviembre de 2026

Este manual permite dejar el sistema funcionando en un equipo nuevo. Tiene dos
caminos: la instalación manual, que es la que se usó durante el desarrollo, y el
despliegue en contenedores, que levanta todo con una sola orden.

---

## 1. Qué se necesita

Para la instalación manual:

- SDK de .NET 8. Se comprueba con `dotnet --version`, que debe responder 8.x.
- SQL Server 2022, edición Developer o Express, con autenticación de Windows.
- SQL Server Management Studio, para ejecutar los guiones.
- Un navegador actualizado.

Para el despliegue en contenedores:

- Un motor compatible con Docker, con Docker Compose incluido.
- Al menos 4 GB de memoria libre. El contenedor de SQL Server exige 2 GB.

Hardware mínimo probado: cuatro núcleos y 8 GB de memoria.

---

## 2. Instalación manual

### 2.1 Base de datos

Abra SQL Server Management Studio y ejecute, en orden numérico, los archivos de
la carpeta `database`, empezando por `01_crear_base_de_datos.sql`. Todos son
idempotentes: volver a ejecutarlos no borra ni duplica datos.

El guion `12b_restaurar_suite.sql` es opcional y no debe ejecutarse: restaura un
tipo de habitación que se eliminó del catálogo.

Para comprobar que quedó bien:

```sql
USE HotelReservas;
SELECT COUNT(*) AS habitaciones FROM HABITACION;
SELECT name FROM sys.procedures ORDER BY name;
```

Deben aparecer las habitaciones de prueba y los procedimientos
`sp_ConfirmarReserva` y `sp_LiberarReservasVencidas`.

### 2.2 Configuración del servicio

Abra `HotelReservas.Api/appsettings.json` y revise dos valores.

La cadena de conexión, que por omisión apunta a la instancia local con
autenticación de Windows:

```json
"ConnectionStrings": {
  "HotelReservas": "Server=localhost;Database=HotelReservas;Trusted_Connection=True;TrustServerCertificate=True;"
}
```

Y la clave de firma de los tokens, que trae un texto de ejemplo y debe
reemplazarse por una cadena propia de al menos 32 caracteres antes de publicar
el sistema:

```json
"Jwt": { "Key": "..." }
```

La sección `Asistente` puede quedarse con la clave vacía. En ese caso responde el
intérprete de reglas, que no necesita internet ni cuenta.

### 2.3 Puesta en marcha

En una consola:

```powershell
cd HotelReservas.Api
dotnet run
```

El servicio queda en `http://localhost:5080`. Al arrancar verifica el esquema y
escribe en la consola el nombre del guion que falte, si alguno no se ejecutó.

En otra consola:

```powershell
cd HotelReservas.Web
dotnet run
```

El portal queda en `http://localhost:5180`.

### 2.4 Comprobación

Abra estas tres direcciones:

- `http://localhost:5080/health` debe responder con estado `Healthy`.
- `http://localhost:5080/health/ready` debe responder `Healthy` y consultar la
  base de datos de verdad. Si responde `Unhealthy`, el problema está en SQL
  Server o en la cadena de conexión.
- `http://localhost:5080/swagger` muestra el catálogo de recursos del servicio.

Entre al portal, elija fechas y verifique que aparezcan habitaciones con precio.

### 2.5 Usuarios de prueba

La contraseña de todos es `Hotel2026*` y debe cambiarse antes de cualquier uso
real.

- `admin@hotel.com`, perfil ADMINISTRADOR.
- `recepcion1@hotel.com` y `recepcion2@hotel.com`, perfil RECEPCIONISTA.

---

## 3. Despliegue en contenedores

### 3.1 Variables

Copie la plantilla y cambie los valores:

```powershell
copy .env.ejemplo .env
```

`SQL_PASSWORD` debe tener al menos ocho caracteres con mayúsculas, minúsculas,
dígitos y un símbolo, o el contenedor de SQL Server no arranca. `JWT_KEY` debe
tener al menos 32 caracteres.

El archivo `.env` no se sube al repositorio: ya está excluido.

### 3.2 Levantar el sistema

```powershell
docker compose up -d --build
```

La primera vez tarda varios minutos porque descarga las imágenes y compila. El
orden lo maneja la composición: primero la base de datos, después la carga del
esquema, después la API y por último la pasarela.

Para seguir el avance:

```powershell
docker compose ps
docker compose logs -f esquema
```

El servicio `esquema` ejecuta los guiones y termina. Que aparezca como `exited`
con código 0 es lo correcto.

### 3.3 Direcciones

- Portal: `http://localhost:8090`
- Servicio y Swagger: `http://localhost:8080`
- Salud: `http://localhost:8080/health/ready`

### 3.4 Detener y volver a levantar

```powershell
docker compose down        # conserva los datos en el volumen
docker compose up -d       # vuelve a levantar con los mismos datos
docker compose down -v     # borra también los datos
```

---

## 4. Ejecutar el banco de pruebas

Con el servicio en marcha:

```powershell
dotnet run --project pruebas -- --api http://localhost:5080
```

Opciones: `--concurrencia N` cambia cuántas reservas simultáneas se lanzan sobre
la misma habitación, `--rendimiento N` cuántas peticiones hace la prueba de
carga, y `--salida ruta` dónde deja los archivos.

Al terminar escribe `evidencias.md` y `evidencias.json`. El código de salida es 0
si todas las pruebas pasaron y 1 si alguna falló.

---

## 5. Problemas frecuentes

**La API arranca y escribe que falta un guion.** Ejecute en SQL Server el guion
que nombra el mensaje y reinicie el servicio.

**`/health/ready` responde Unhealthy.** SQL Server no está respondiendo, la
cadena de conexión apunta a otra instancia, o el usuario no tiene permiso sobre
la base `HotelReservas`.

**El portal carga pero no muestra habitaciones.** Revise que el servicio esté
arriba y que `wwwroot/appsettings.json` del cliente apunte a la dirección
correcta. Si el valor queda vacío, el cliente usa su propio origen, que es lo que
corresponde en el despliegue con contenedores.

**Windows bloquea la ejecución con un mensaje de directiva de control de
aplicaciones.** Pase `Get-ChildItem -Recurse -File | Unblock-File` en la carpeta
del proyecto, borre `bin` y `obj` y vuelva a compilar. Si persiste, trabaje desde
una carpeta fuera del Escritorio, por ejemplo `C:\Proyectos`.

**`dotnet build` falla al restaurar paquetes.** El equipo no tiene salida a
NuGet. Compruebe la conexión o configure el proxy de la red.

**El contenedor de SQL Server se reinicia solo.** Casi siempre es la contraseña:
debe cumplir la política de complejidad. Revise con `docker compose logs
base-de-datos`.
