# El repositorio quedó incompleto

Revisé lo que hay publicado en GitHub y faltan archivos. No es un detalle menor:
sin ellos el proyecto no compila ni arranca para quien lo descargue.

## Qué falta en GitHub

`HotelReservas.Web/wwwroot/index.html`, que es la página que carga toda la
aplicación Blazor. Sin ese archivo la aplicación web no existe.

`HotelReservas.Web/_Imports.razor`, que declara los espacios de nombres que usan
todas las páginas. Sin él, ninguna página compila.

`HotelReservas.Web/wwwroot/prototipo.html`, el prototipo estático.

Cuatro imágenes del catálogo: `suite.svg`, `suite-3.svg`, `suite-foto.jpg` y
`triple-foto.jpg`.

Los tres archivos `.gitignore`, de modo que si alguien compila dentro de esa
carpeta, las carpetas `bin` y `obj` se van a subir al repositorio.

Y los dos archivos de ayuda, `COMO_SUBIRLO.md` y `subir-a-github.ps1`.

## Qué pasó

El commit se hizo sobre una carpeta a la que le faltaban esos archivos. El zip sí
los traía, de modo que lo más probable es que la descompresión en Windows haya
quedado a medias, que es algo que pasa cuando se arrastran los archivos desde la
ventana del zip en lugar de usar Extraer todo.

## Cómo se arregla

Vuelva a descomprimir el zip sobre la misma carpeta que ya tiene, aceptando
reemplazar los archivos existentes. No pierde nada: el historial de git vive en la
subcarpeta oculta `.git` y no se toca.

Después, parada en esa carpeta, ejecute:

    powershell -ExecutionPolicy Bypass -File .\verificar-y-subir.ps1

Ese script primero compara la carpeta contra `manifiesto.txt`, que trae la lista
completa de los archivos del proyecto. Si falta alguno, se lo dice por nombre y no
sube nada. Solo cuando está completo crea el commit y hace el push.

## Para descomprimir bien en Windows

Haga clic derecho sobre el zip, elija Extraer todo, y extraiga a una carpeta
vacía. No abra el zip con doble clic para arrastrar archivos hacia afuera, porque
esa forma es la que suele dejar archivos por el camino sin avisar.
