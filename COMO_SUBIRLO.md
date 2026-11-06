# Cómo publicar esta carpeta en GitHub

Todo quedó preparado: el `.gitignore`, el `README.md`, la solución con los dos
proyectos de la versión web y los guiones de base de datos. Falta únicamente el
paso que necesita sus credenciales de GitHub, que yo no tengo.

## La forma corta

Descomprima esta carpeta donde quiera conservarla, por ejemplo en
`C:\Proyectos\HotelReservasWeb`, abra PowerShell ahí y ejecute:

    .\subir-a-github.ps1

Si Windows no deja ejecutar el archivo, use:

    powershell -ExecutionPolicy Bypass -File .\subir-a-github.ps1

La primera vez GitHub abre el navegador para que usted inicie sesión. Autorice y
el script sigue solo.

## La forma manual, si prefiere ver cada paso

    cd C:\Proyectos\HotelReservasWeb
    git init -b main
    git add -A
    git commit -m "Version web del sistema de reservas"
    git remote add origin https://github.com/gustavo8015/HotelReservasSistemasDistribuidos.git
    git push -u origin main

## Si el push es rechazado

Si aparece un error 403 o dice que no tiene permiso, la sesión de GitHub que abrió
el navegador no es la dueña del repositorio ni tiene permiso de escritura sobre
él. El repositorio está bajo la cuenta `gustavo8015`, de modo que hay que
autenticarse con esa cuenta, o pedirle a su dueño que agregue la suya como
colaboradora en Settings, Collaborators.

Si en cambio dice que el repositorio ya tiene contenido, ejecute primero
`git pull --rebase origin main` y vuelva a intentar el push.

## Qué se incluyó y qué no

Se incluyó la API, la aplicación Blazor, los guiones de base de datos, el
prototipo estático y el sistema de diseño.

No se incluyó la aplicación móvil, porque este repositorio es solo de la versión
web. Tampoco se incluyeron las carpetas `bin` y `obj`, que son salidas de
compilación y se vuelven a generar solas, ni la carpeta `docs` con el documento
maestro, los diagramas y la auditoría, porque el repositorio es público y esos
documentos son su trabajo escrito. Si prefiere publicarlos también, copie la
carpeta `docs` dentro de esta y vuelva a ejecutar el script.

## Revise antes de subir

No hay contraseñas ni claves en los archivos: la cadena de conexión usa
autenticación de Windows y las claves del asistente están vacías. El valor de
`Jwt:Key` en `appsettings.json` es un texto de ejemplo, así que si algún día pone
ahí una clave real, acuérdese de que este repositorio es público.
