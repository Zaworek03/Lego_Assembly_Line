# ================================================================
#  Stop-LiniaProdukcyjna.ps1
#  Zatrzymuje Dashboard + Middleware dzialajace w tle.
# ================================================================

Write-Host "Zatrzymuje Linia Montazowa (Dashboard + Middleware)..."

$dash = Get-Process -Name "LiniaProdukcyjnaDashboard" -ErrorAction SilentlyContinue
$mid  = Get-Process -Name "PlcToDbMiddleware"        -ErrorAction SilentlyContinue

if ($dash) { $dash | Stop-Process -Force -ErrorAction SilentlyContinue; Write-Host "Dashboard zatrzymany." } else { Write-Host "Dashboard nie dzialal." }
if ($mid)  { $mid  | Stop-Process -Force -ErrorAction SilentlyContinue; Write-Host "Middleware zatrzymany." } else { Write-Host "Middleware nie dzialal." }

Write-Host "Gotowe."
Start-Sleep -Seconds 2
