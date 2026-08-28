# ================================================================
#  Start-LiniaProdukcyjna.ps1
#  Uruchamia/restartuje Dashboard (Blazor) + Middleware (PLC<->SQL)
#  w TLE (bez widocznych okien konsoli).
#  Uzycie: odpal ten skrypt (lub skrot "Uruchom Linia Montazowa")
#          po kazdym wlaczeniu komputera / potrzebie restartu.
#  Zatrzymanie: skrot "Zatrzymaj Linia Montazowa" albo Stop-LiniaProdukcyjna.ps1
# ================================================================

$root     = "C:\Users\Rizz & Bricks inc\Documents\Lego_Assembly_Line\WEB"
$dashDir  = Join-Path $root "Dashboard\bin\Debug\net10.0"
$midDir   = Join-Path $root "Middleware\bin\Debug\net10.0"
$dashExe  = Join-Path $dashDir "LiniaProdukcyjnaDashboard.exe"
$midExe   = Join-Path $midDir  "PlcToDbMiddleware.exe"
$dashUrl  = "http://localhost:5200"
$logsDir  = Join-Path $root "logs"

if (-not (Test-Path $logsDir)) { New-Item -ItemType Directory -Path $logsDir | Out-Null }
$logFile = Join-Path $logsDir "launcher.log"

function Log($msg) {
    $line = "[$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')] $msg"
    Write-Host $msg
    Add-Content -Path $logFile -Value $line
}

Log "=== Linia Montazowa LEGO - restart uslug (tryb w tle) ==="

# ── 1. Zatrzymaj stare instancje (jesli dzialaja) ──
Log "Zatrzymuje stare procesy (jesli dzialaja)..."
Get-Process -Name "LiniaProdukcyjnaDashboard" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Get-Process -Name "PlcToDbMiddleware"        -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# ── 2. Sprawdz czy pliki .exe istnieja (czy projekt jest zbudowany) ──
if (-not (Test-Path $dashExe)) {
    Log "BLAD: Nie znaleziono $dashExe - zbuduj projekt: dotnet build `"$root\Dashboard\LiniaProdukcyjnaDashboard.csproj`""
    exit 1
}
if (-not (Test-Path $midExe)) {
    Log "BLAD: Nie znaleziono $midExe - zbuduj projekt: dotnet build `"$root\Middleware\PlcToDbMiddleware.csproj`""
    exit 1
}

# ── 3. Uruchom Dashboard (Blazor Web) w tle, bez okna ──
Log "Uruchamiam Dashboard na $dashUrl (w tle)..."
Start-Process -FilePath $dashExe -ArgumentList "--urls $dashUrl" -WorkingDirectory $dashDir `
    -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $logsDir "dashboard.log") `
    -RedirectStandardError  (Join-Path $logsDir "dashboard.err.log")

# ── 4. Uruchom Middleware (polaczenie PLC <-> SQL) w tle, bez okna ──
Log "Uruchamiam Middleware (polaczenie z PLC) w tle..."
Start-Process -FilePath $midExe -WorkingDirectory $midDir `
    -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $logsDir "middleware.log") `
    -RedirectStandardError  (Join-Path $logsDir "middleware.err.log")

# ── 5. Poczekaj az strona wstanie, potem otworz przegladarke ──
Log "Czekam az strona wstanie..."
$ready = $false
for ($i = 0; $i -lt 20; $i++) {
    Start-Sleep -Milliseconds 500
    try {
        $resp = Invoke-WebRequest -Uri $dashUrl -UseBasicParsing -TimeoutSec 1
        if ($resp.StatusCode -eq 200) { $ready = $true; break }
    } catch {}
}

Start-Process $dashUrl
if ($ready) {
    Log "Gotowe! Strona dziala pod $dashUrl (dziala w tle, brak okien)."
} else {
    Log "UWAGA: strona jeszcze sie nie odpowiada po 10s - sprawdz logs\dashboard.err.log"
}

Log "Middleware dziala w tle - status sprawdzisz w logs\middleware.log (np. czy polaczyl sie z PLC)."
