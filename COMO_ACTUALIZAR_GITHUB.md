# Cómo subir esta actualización a GitHub

El repositorio quedó con la versión del 6 de noviembre. Falta todo lo que se
hizo para el proyecto final: tolerancia a fallos, verificación de salud,
despliegue en contenedores y el banco de pruebas.

## Qué trae esta carpeta

Es el repositorio completo y actualizado, no solo los archivos nuevos. Lo que
cambió frente a lo que está publicado:

Archivos nuevos: `HotelReservas.Api/Servicios/Resiliencia.cs`,
`HotelReservas.Web/Services/Resiliencia.cs`,
`HotelReservas.Web/Layout/AvisoServicio.razor`, los dos `Dockerfile`,
`despliegue/nginx.conf`, `docker-compose.yml`, `.env.ejemplo`, la carpeta
`pruebas` completa y la carpeta `docs` con el manual.

Archivos modificados: `HotelReservas.Api/Program.cs` con los recursos de salud y
los reintentos, `HotelReservas.Web/Program.cs`, `ApiClient.cs`,
`MainLayout.razor`, la hoja de estilos, el `README.md` con cuatro secciones
nuevas, el `.gitignore` que ahora excluye el `.env` y el `evidencias.json`, y la
solución, que ahora incluye el proyecto de pruebas.

## Cómo subirlo

Descomprima esta carpeta **sobre la que ya tiene conectada al repositorio**, que
es `...\HotelReservasWeb_completo\HotelReservasWeb`, aceptando reemplazar. No
pierde el historial: vive en la subcarpeta oculta `.git` y no se toca.

Después, parada en esa carpeta:

    powershell -ExecutionPolicy Bypass -File .\verificar-y-subir.ps1

El script compara la carpeta contra `manifiesto.txt`, comprueba que git esté
tomando todos los archivos, le pregunta el mensaje del commit y hace el push.
Esta vez no hace falta `--force`: el historial es el mismo y el commit nuevo va
encima.

## Si prefiere hacerlo a mano

    cd ...\HotelReservasWeb_completo\HotelReservasWeb
    git add -A
    git status
    git commit -m "Tolerancia a fallos, salud, contenedores y banco de pruebas"
    git push

El `git status` antes del commit le muestra la lista de lo que va a subir, por si
quiere revisarla.

## Una decisión que es suya

No incluí el documento del proyecto final ni el PDF. El repositorio es público y
ese es su trabajo escrito. Si prefiere publicarlo para que el profesor lo
encuentre junto al código, copie el `.docx` y el `.pdf` dentro de `docs` y vuelva
a ejecutar el script.
