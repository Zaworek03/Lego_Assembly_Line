# ============================================================
#  Linia Montazowa - zatrzymanie systemu
#  ------------------------------------------------------------
#  Strona i middleware chodza w tle, bez okien konsoli, wiec nie
#  da sie ich zamknac krzyzykiem - od tego jest ten skrypt.
#
#  Bazy NIE ruszamy: LocalDB gasi sie sama, a jej reczne
#  zatrzymywanie bylo dotad zrodlem wiekszosci problemow.
# ============================================================
$ErrorActionPreference = 'Continue'

Write-Host ''
Write-Host '  ==========================================' -ForegroundColor DarkCyan
Write-Host '   LINIA MONTAZOWA - zatrzymywanie'          -ForegroundColor DarkCyan
Write-Host '  ==========================================' -ForegroundColor DarkCyan
Write-Host ''

$cokolwiek = $false
foreach ($nazwa in 'LiniaProdukcyjnaDashboard', 'PlcToDbMiddleware') {
    $proc = Get-Process -Name $nazwa -ErrorAction SilentlyContinue
    if (-not $proc) {
        Write-Host "   -    $nazwa - nie dzialal" -ForegroundColor Gray
        continue
    }
    $cokolwiek = $true
    foreach ($p in $proc) {
        try {
            Stop-Process -Id $p.Id -Force -ErrorAction Stop
            Write-Host "   OK   $nazwa (PID $($p.Id)) zatrzymany" -ForegroundColor Green
        } catch {
            # Proces uruchomiony z podwyzszonymi uprawnieniami nie da sie zamknac
            # ze zwyklego okna - mowimy wprost, co z tym zrobic.
            Write-Host "   !    $nazwa (PID $($p.Id)) - brak dostepu." -ForegroundColor Yellow
            Write-Host "        Zostal uruchomiony jako administrator. W oknie administratora:" -ForegroundColor Yellow
            Write-Host "        Stop-Process -Id $($p.Id) -Force" -ForegroundColor Yellow
        }
    }
}

if (-not $cokolwiek) { Write-Host '' ; Write-Host '   Nic nie bylo uruchomione.' -ForegroundColor Gray }

Write-Host ''
Start-Sleep -Seconds 2
