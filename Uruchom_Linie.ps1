# ============================================================
#  Linia Montazowa - uruchamianie calego systemu
#  ------------------------------------------------------------
#  Podnosi po kolei wszystko, czego strona potrzebuje do pracy:
#    1. LocalDB (baza BazaDanychRB)
#    2. Middleware  - most PLC <-> baza
#    3. Dashboard   - strona na http://localhost:5000
#
#  Nie uruchamiaj tego jako administrator - patrz komentarz nizej.
#  Bez diakrytykow celowo: Windows PowerShell 5.1 czyta pliki .ps1
#  bez BOM jako ANSI i polskie znaki rozsypaly by sie na konsoli.
# ============================================================
[CmdletBinding()]
param(
    # Wymusza 'dotnet build' obu projektow nawet gdy pliki .exe juz istnieja.
    [switch]$Buduj,
    # Ubija dzialajace instancje i stawia je od nowa.
    [switch]$Restart,
    # Nie otwiera przegladarki na koniec.
    [switch]$BezPrzegladarki
)

$ErrorActionPreference = 'Stop'
$katalog = Split-Path -Parent $MyInvocation.MyCommand.Path

$dashboardProj = Join-Path $katalog 'WEB\Dashboard'
$middlewareProj = Join-Path $katalog 'WEB\Middleware'
$dashboardExe  = Join-Path $dashboardProj  'bin\Debug\net10.0\LiniaProdukcyjnaDashboard.exe'
$middlewareExe = Join-Path $middlewareProj 'bin\Debug\net10.0\PlcToDbMiddleware.exe'

$adres = 'http://localhost:5000'
$instancjaLocalDb = 'MSSQLLocalDB'
$polaczenie = "Data Source=(localdb)\$instancjaLocalDb;Initial Catalog=BazaDanychRB;" +
              "Integrated Security=True;Connect Timeout=30;Encrypt=False;TrustServerCertificate=True;"

$logi = Join-Path $katalog 'logs'

# Oba programy startuja BEZ widocznej konsoli i sa odpiete od tego okna - jego
# zamkniecie ich nie ubija. Wyjscie, ktore normalnie leci na ekran, zapisujemy
# do plikow w logs\, zeby przy problemach nadal bylo co czytac.
function Uruchom($exe, $katalogRoboczy, $nazwaLogu) {
    if (-not (Test-Path $logi)) { New-Item -ItemType Directory -Path $logi | Out-Null }
    Start-Process -FilePath $exe -WorkingDirectory $katalogRoboczy `
                  -WindowStyle Hidden `
                  -RedirectStandardOutput (Join-Path $logi "$nazwaLogu.log") `
                  -RedirectStandardError  (Join-Path $logi "$nazwaLogu.err.log")
}

function Krok($tekst)   { Write-Host "`n[$([DateTime]::Now.ToString('HH:mm:ss'))] $tekst" -ForegroundColor Cyan }
function Ok($tekst)     { Write-Host "   OK   $tekst" -ForegroundColor Green }
function Uwaga($tekst)  { Write-Host "   !    $tekst" -ForegroundColor Yellow }
function Blad($tekst)   { Write-Host "   BLAD $tekst" -ForegroundColor Red }

Write-Host ''
Write-Host '  ==========================================' -ForegroundColor DarkCyan
Write-Host '   LINIA MONTAZOWA - uruchamianie systemu'   -ForegroundColor DarkCyan
Write-Host '  ==========================================' -ForegroundColor DarkCyan

# ------------------------------------------------------------
# 0. Uprawnienia
# ------------------------------------------------------------
# LocalDB tworzy osobna instancje dla procesu z podwyzszonymi uprawnieniami
# i nie wpuszcza do niej zwyklych procesow. Raz uruchomiona "jako administrator"
# blokuje pliki bazy i normalna instancja nie ma jak wstac - system wyglada
# wtedy na kompletnie zepsuty, a jedynym lekarstwem jest ubicie tamtego procesu
# z okna administratora. Dlatego wychodzimy od razu, zamiast to naprawiac pozniej.
$tozsamosc = [Security.Principal.WindowsIdentity]::GetCurrent()
$rola = New-Object Security.Principal.WindowsPrincipal($tozsamosc)
if ($rola.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Blad 'Uruchomiono jako administrator.'
    Write-Host ''
    Write-Host '   LocalDB podniesiona z uprawnieniami administratora jest niedostepna' -ForegroundColor Yellow
    Write-Host '   dla zwyklych procesow - strona i middleware nie polacza sie z baza.' -ForegroundColor Yellow
    Write-Host '   Zamknij to okno i uruchom skrypt normalnie (podwojne klikniecie).'   -ForegroundColor Yellow
    Write-Host ''
    # Pauze robi plik .bat (pause przy errorlevel 1) - Read-Host wywalilby sie
    # w trybie nieinteraktywnym, np. przy uruchomieniu z zadania harmonogramu.
    exit 1
}

# ------------------------------------------------------------
# 1. Ewentualne zatrzymanie starych instancji
# ------------------------------------------------------------
if ($Restart) {
    Krok 'Zatrzymywanie dzialajacych instancji'
    foreach ($nazwa in 'LiniaProdukcyjnaDashboard', 'PlcToDbMiddleware') {
        $proc = Get-Process -Name $nazwa -ErrorAction SilentlyContinue
        if ($proc) {
            try { $proc | Stop-Process -Force -ErrorAction Stop; Ok "$nazwa zatrzymany" }
            catch { Uwaga "$nazwa - brak dostepu (czy nie chodzi jako administrator?)" }
        }
    }
    Start-Sleep -Seconds 2
}

# ------------------------------------------------------------
# 2. Baza danych
# ------------------------------------------------------------
Krok 'Baza danych (LocalDB)'

# Zrodlem prawdy jest udane polaczenie, a nie 'sqllocaldb info'. To polecenie
# potrafi raportowac "Stopped" dla instancji, ktora w tej chwili obsluguje
# klientow (stan w rejestrze rozjezdza sie z rzeczywistoscia, gdy instancje
# podniosl automatycznie pierwszy klient). Probujemy wiec najpierw polaczyc sie,
# a dopiero gdy to zawiedzie - startowac instancje.
function Test-Baza {
    try {
        $conn = New-Object System.Data.SqlClient.SqlConnection $script:polaczenie
        $conn.Open()
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = 'SELECT COUNT(*) FROM Zlecenie_Produkcyjne WHERE IsDeleted = 0'
        $script:aktywneZlecenia = $cmd.ExecuteScalar()
        $conn.Close()
        return $true
    } catch {
        $script:ostatniBladBazy = $_.Exception.Message.Split([Environment]::NewLine)[0]
        return $false
    }
}

$bazaOk = Test-Baza
if (-not $bazaOk) {
    # Natywne polecenia pisza na stderr, co przy ErrorActionPreference = Stop
    # przerywa skrypt mimo poprawnego dzialania - stad lokalne zlagodzenie.
    $poprzedni = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    & sqllocaldb start $instancjaLocalDb | Out-Null
    $ErrorActionPreference = $poprzedni

    Start-Sleep -Seconds 2
    $bazaOk = Test-Baza
}

if (-not $bazaOk) {
    # Najczestsza przyczyna: osierocony proces sqlservr po poprzedniej sesji
    # trzyma pliki instancji, wiec nowa nie moze zarejestrowac swojego potoku.
    # Taki proces bywa chroniony i nie da sie go ubic bez uprawnien administratora,
    # dlatego robimy to jednym wywolaniem z UAC - reszta skryptu zostaje zwykla,
    # bo LocalDB MUSI wstac w kontekscie normalnego uzytkownika.
    #
    # Instancje SQL Express pomijamy: chodza jako usluga i nie maja z tym nic wspolnego.
    $pidyUslug = @(Get-CimInstance Win32_Service -Filter "Name LIKE 'MSSQL%'" -ErrorAction SilentlyContinue |
                   Select-Object -ExpandProperty ProcessId)
    $sieroty = @(Get-Process sqlservr -ErrorAction SilentlyContinue |
                 Where-Object { $pidyUslug -notcontains $_.Id })
    $script:sierotyPidy = $sieroty.Id

    if ($sieroty.Count -gt 0) {
        Uwaga "Osierocony proces LocalDB blokuje baze (PID: $($sieroty.Id -join ', '))."
        Uwaga 'Zamkniecie go wymaga uprawnien administratora - pojawi sie pytanie systemu.'
        $lista = ($sieroty.Id -join ',')
        try {
            Start-Process powershell -Verb RunAs -Wait -WindowStyle Hidden `
                -ArgumentList "-NoProfile -Command `"Stop-Process -Id $lista -Force`""
            Start-Sleep -Seconds 3
            $poprzedni = $ErrorActionPreference
            $ErrorActionPreference = 'Continue'
            & sqllocaldb start $instancjaLocalDb | Out-Null
            $ErrorActionPreference = $poprzedni
            Start-Sleep -Seconds 2
            $bazaOk = Test-Baza
            if ($bazaOk) { Ok 'Blokada usunieta, baza wstala' }
        } catch {
            Uwaga 'Nie udalo sie podniesc uprawnien - zamknij ten proces recznie.'
        }
    }
}

if ($bazaOk) {
    Ok "Polaczenie dziala (aktywnych zlecen: $aktywneZlecenia)"
} else {
    Blad "Baza nieosiagalna: $ostatniBladBazy"
    if ($sierotyPidy) {
        Uwaga 'Otworz PowerShell jako administrator i wykonaj DOKLADNIE to:'
        Uwaga "   Stop-Process -Id $($sierotyPidy -join ',') -Force"
        # Celowo konkretne numery, a nie 'Get-Process sqlservr | Stop-Process':
        # ten skrot ubilby takze proces uslugi SQL Express, ktory z nasza baza
        # nie ma nic wspolnego.
        Uwaga 'a potem uruchom ten skrypt ponownie - juz BEZ administratora.'
    } else {
        Uwaga 'Sprawdz, czy LocalDB jest zainstalowana: sqllocaldb info'
    }
    exit 1
}

# ------------------------------------------------------------
# 3. Kompilacja (tylko gdy trzeba)
# ------------------------------------------------------------
foreach ($p in @(
    @{ Nazwa = 'Middleware'; Projekt = $middlewareProj; Exe = $middlewareExe },
    @{ Nazwa = 'Dashboard';  Projekt = $dashboardProj;  Exe = $dashboardExe  })) {

    if ($Buduj -or -not (Test-Path $p.Exe)) {
        Krok "Kompilacja: $($p.Nazwa)"
        Push-Location $p.Projekt
        try {
            & dotnet build -v q --nologo | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "dotnet build zwrocil kod $LASTEXITCODE" }
            Ok 'Kompilacja zakonczona'
        } catch {
            Blad "$($p.Nazwa): $($_.Exception.Message)"
            Uwaga 'Czesta przyczyna: aplikacja jest uruchomiona i blokuje plik .exe. Sprobuj z -Restart.'
            Pop-Location
            Write-Host ''
            # Pauze robi plik .bat (pause przy errorlevel 1) - Read-Host wywalilby sie
            # w trybie nieinteraktywnym, np. przy uruchomieniu z zadania harmonogramu.
            exit 1
        }
        Pop-Location
    }
}

# ------------------------------------------------------------
# 4. Middleware
# ------------------------------------------------------------
Krok 'Middleware (PLC <-> baza)'
if (Get-Process -Name 'PlcToDbMiddleware' -ErrorAction SilentlyContinue) {
    Ok 'Juz dziala - pomijam'
} else {
    Uruchom $middlewareExe $middlewareProj 'middleware'
    Ok "Uruchomiony w tle (log: logs\middleware.log)"
    # Brak polaczenia z PLC nie jest bledem krytycznym: middleware ponawia
    # proby co 5 s, wiec mozna go wlaczyc przed sterownikiem.
    Uwaga 'Brak PLC nie zatrzymuje systemu - middleware ponawia laczenie co 5 s'
}

# ------------------------------------------------------------
# 5. Dashboard
# ------------------------------------------------------------
Krok "Strona ($adres)"
$dziala = Get-Process -Name 'LiniaProdukcyjnaDashboard' -ErrorAction SilentlyContinue
if ($dziala) {
    # Sama obecnosc procesu nie wystarczy. Instancja uruchomiona z zlego katalogu
    # (dwuklik w bin\, stary skrypt startowy) wstaje i odpowiada, ale nie serwuje
    # wlasnych zasobow - /app.css i /app.js daja 404, przez co strona wyglada jak
    # wersja sprzed calego frontendu. Zamiast otwierac taka przegladarke w milczeniu,
    # mowimy wprost, co jest nie tak.
    $zasobyOk = $false
    try {
        $test = Invoke-WebRequest "$adres/app.css" -UseBasicParsing -TimeoutSec 4
        $zasobyOk = ($test.StatusCode -eq 200)
    } catch { }

    if ($zasobyOk) {
        Ok 'Juz dziala - pomijam'
    } else {
        Blad "Dziala instancja (PID $($dziala.Id -join ', ')), ale nie serwuje wlasnych stylow."
        Uwaga 'To najczesciej kopia uruchomiona z katalogu bin\ albo z uprawnieniami administratora.'
        Uwaga 'Zamknij ja i uruchom ten skrypt ponownie:'
        Uwaga "   Stop-Process -Id $($dziala.Id -join ',') -Force"
        exit 1
    }
} else {
    $zajety = Get-NetTCPConnection -State Listen -LocalPort 5000 -ErrorAction SilentlyContinue
    if ($zajety) {
        Blad "Port 5000 zajety przez PID $($zajety.OwningProcess) - strona nie wstanie."
        Write-Host ''
        # Pauze robi plik .bat (pause przy errorlevel 1) - Read-Host wywalilby sie
        # w trybie nieinteraktywnym, np. przy uruchomieniu z zadania harmonogramu.
        exit 1
    }
    Uruchom $dashboardExe $dashboardProj 'dashboard'
    Ok "Uruchomiona w tle (log: logs\dashboard.log)"
}

# ------------------------------------------------------------
# 6. Czekamy az strona zacznie odpowiadac
# ------------------------------------------------------------
Krok 'Czekam na gotowosc strony'
$gotowa = $false
foreach ($proba in 1..30) {
    Start-Sleep -Milliseconds 700
    try {
        $odp = Invoke-WebRequest $adres -UseBasicParsing -TimeoutSec 3
        if ($odp.StatusCode -eq 200) { $gotowa = $true; break }
    } catch { }
}

if ($gotowa) {
    Ok "Strona odpowiada: $adres"
    if (-not $BezPrzegladarki) { Start-Process $adres }
} else {
    Uwaga "Strona nie odpowiedziala w 21 s - sprawdz jej okno konsoli."
}

Write-Host ''
Write-Host '  ------------------------------------------' -ForegroundColor DarkCyan
Write-Host '   Gotowe. Oba programy pracuja w tle, bez okien konsoli.'   -ForegroundColor Green
Write-Host '   To okno mozna zamknac - nic sie nie wylaczy.'            -ForegroundColor Gray
Write-Host "   Logi: $logi"                                          -ForegroundColor Gray
Write-Host '   Zatrzymanie: Zatrzymaj_Linie.bat'                         -ForegroundColor Gray
Write-Host '  ------------------------------------------' -ForegroundColor DarkCyan
Write-Host ''
Start-Sleep -Seconds 3
