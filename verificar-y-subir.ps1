<#
    verificar-y-subir.ps1
    Comprueba que esta carpeta tenga TODOS los archivos del proyecto y, si no falta
    ninguno, crea el commit y lo sube a GitHub.

    Úselo así, parado en esta carpeta:

        powershell -ExecutionPolicy Bypass -File .\verificar-y-subir.ps1
#>

$ErrorActionPreference = 'Stop'
$remoto = 'https://github.com/gustavo8015/HotelReservasSistemasDistribuidos.git'

Write-Host ''
Write-Host 'Hotel Reservas - verificacion y publicacion' -ForegroundColor Cyan
Write-Host '-------------------------------------------'

# 1. Comparar contra el manifiesto
if (-not (Test-Path 'manifiesto.txt')) {
    Write-Host 'No se encontro manifiesto.txt. Esta parado en la carpeta equivocada.' -ForegroundColor Red
    Write-Host "Carpeta actual: $(Get-Location)"
    exit 1
}

$esperados = Get-Content 'manifiesto.txt' | Where-Object { $_.Trim() -ne '' }
$faltan = @()
foreach ($ruta in $esperados) {
    $local = $ruta -replace '/', '\'
    if (-not (Test-Path -LiteralPath $local)) { $faltan += $ruta }
}

Write-Host ("Archivos esperados: {0}" -f $esperados.Count)
Write-Host ("Archivos presentes: {0}" -f ($esperados.Count - $faltan.Count))

if ($faltan.Count -gt 0) {
    Write-Host ''
    Write-Host ("FALTAN {0} archivos:" -f $faltan.Count) -ForegroundColor Red
    $faltan | ForEach-Object { Write-Host "   $_" }
    Write-Host ''
    Write-Host 'Vuelva a descomprimir el zip sobre esta misma carpeta, aceptando reemplazar,' -ForegroundColor Yellow
    Write-Host 'y ejecute este script otra vez. No se subio nada.' -ForegroundColor Yellow
    exit 1
}

Write-Host 'No falta ningun archivo.' -ForegroundColor Green

# 2. Quitar la marca de archivo descargado
Get-ChildItem -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

# 3. Repositorio local
if (-not (Test-Path '.git')) { git init -b main | Out-Null } else { git checkout -B main | Out-Null }
if (-not (git config user.name))  { git config user.name  'Gustavo Cardona' }
if (-not (git config user.email)) { git config user.email 'gustavo8015@users.noreply.github.com' }

# 4. Confirmar
git add -A
$versionados = @(git ls-files)
Write-Host ("Archivos que quedaran versionados: {0}" -f $versionados.Count)

# Segunda comprobacion: el archivo existe en disco pero git no lo esta tomando.
# Casi siempre es un .gitignore, propio o global, que lo esta excluyendo.
$indice = @{}
foreach ($v in $versionados) { $indice[$v] = $true }
$ignorados = @()
foreach ($ruta in $esperados) {
    if ($ruta -eq 'manifiesto.txt') { continue }
    if (-not $indice.ContainsKey($ruta)) { $ignorados += $ruta }
}
if ($ignorados.Count -gt 0) {
    Write-Host ''
    Write-Host ("Hay {0} archivos en disco que git NO esta versionando:" -f $ignorados.Count) -ForegroundColor Red
    foreach ($r in $ignorados) {
        $quien = git check-ignore -v -- $r 2>$null
        if ($quien) { Write-Host "   $r   <- excluido por: $quien" }
        else        { Write-Host "   $r   <- sin regla de exclusion conocida" }
    }
    Write-Host ''
    Write-Host 'No se subio nada. Mandeme este listado y lo resolvemos.' -ForegroundColor Yellow
    exit 1
}

if (git diff --cached --quiet) {
    Write-Host 'No hay cambios nuevos que confirmar.'
} else {
    git commit -m 'Completa la version web: paginas, _Imports, index.html e imagenes faltantes' | Out-Null
    Write-Host 'Commit creado.' -ForegroundColor Green
}

# 5. Remoto y subida
$actual = git remote get-url origin 2>$null
if ($actual) { git remote set-url origin $remoto } else { git remote add origin $remoto }

Write-Host ''
Write-Host 'Subiendo. Si se abre el navegador, autorice y espere.' -ForegroundColor Yellow
git push -u origin main

Write-Host ''
Write-Host 'Listo. Revise https://github.com/gustavo8015/HotelReservasSistemasDistribuidos' -ForegroundColor Green
