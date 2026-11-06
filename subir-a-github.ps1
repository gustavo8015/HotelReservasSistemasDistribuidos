<#
    subir-a-github.ps1
    Publica esta carpeta en el repositorio
    https://github.com/gustavo8015/HotelReservasSistemasDistribuidos

    Ejecútelo desde PowerShell, parado en esta misma carpeta:

        .\subir-a-github.ps1

    La primera vez GitHub le va a pedir que inicie sesión. Se abre el navegador,
    usted autoriza y el script continúa solo. No hay que escribir contraseñas aquí.
#>

$ErrorActionPreference = 'Stop'
$remoto  = 'https://github.com/gustavo8015/HotelReservasSistemasDistribuidos.git'
$mensaje = 'Version web del sistema de reservas: API, aplicacion Blazor y guiones de base de datos'

Write-Host ''
Write-Host 'Hotel Reservas - publicacion en GitHub' -ForegroundColor Cyan
Write-Host '--------------------------------------'

# 1. Comprobar que git esté instalado
try { git --version | Out-Null }
catch { Write-Host 'No se encontro git. Instalelo desde https://git-scm.com/download/win y vuelva a ejecutar.' -ForegroundColor Red; exit 1 }

# 2. Quitar la marca de archivo descargado de internet
Write-Host 'Quitando la marca de archivo descargado...'
Get-ChildItem -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

# 3. Iniciar el repositorio si hace falta
if (-not (Test-Path '.git')) {
    Write-Host 'Iniciando el repositorio local...'
    git init -b main | Out-Null
} else {
    Write-Host 'Ya existe un repositorio local, se reutiliza.'
    git checkout -B main | Out-Null
}

# 4. Configurar quien firma los commits, si no esta configurado
if (-not (git config user.name))  { git config user.name  'Gustavo Cardona' }
if (-not (git config user.email)) { git config user.email 'gustavo8015@users.noreply.github.com' }

# 5. Preparar el commit
Write-Host 'Preparando los archivos...'
git add -A
if (git diff --cached --quiet) {
    Write-Host 'No hay cambios nuevos que confirmar.'
} else {
    git commit -m $mensaje | Out-Null
    Write-Host 'Commit creado.'
}

# 6. Apuntar al repositorio remoto
$actual = git remote get-url origin 2>$null
if ($actual) { git remote set-url origin $remoto } else { git remote add origin $remoto }
Write-Host "Remoto: $remoto"

# 7. Subir
Write-Host ''
Write-Host 'Subiendo. Si se abre el navegador, autorice el acceso y espere.' -ForegroundColor Yellow
git push -u origin main

Write-Host ''
Write-Host 'Listo. Abra https://github.com/gustavo8015/HotelReservasSistemasDistribuidos' -ForegroundColor Green
