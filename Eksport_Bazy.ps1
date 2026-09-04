# ============================================================
#  Eksport zawartosci bazy BazaDanychRB do plikow CSV
#  ------------------------------------------------------------
#  Zrzuca wszystkie tabele do docs\baza_csv\<Tabela>.csv
#  Uruchom na maszynie z LocalDB (tej, na ktorej dziala linia).
#
#  Bez diakrytykow celowo - PowerShell 5.1 czyta .ps1 bez BOM
#  jako ANSI i polskie znaki rozsypaly by sie na konsoli.
# ============================================================
[CmdletBinding()]
param(
    # Ogranicza liczbe wierszy na tabele (0 = bez limitu).
    # Przydatne dla Realizacja_Produkcji / Wskazniki, jesli urosly.
    [int]$MaxWierszy = 0
)

$ErrorActionPreference = 'Stop'

$polaczenie = "Data Source=(localdb)\MSSQLLocalDB;Initial Catalog=BazaDanychRB;" +
              "Integrated Security=True;Encrypt=False;TrustServerCertificate=True;Connect Timeout=30"

$katalog = Split-Path -Parent $MyInvocation.MyCommand.Path
$wyjscie = Join-Path $katalog 'docs\baza_csv'
New-Item -ItemType Directory -Path $wyjscie -Force | Out-Null

Write-Host ''
Write-Host '  ==========================================' -ForegroundColor DarkCyan
Write-Host '   EKSPORT BAZY BazaDanychRB -> CSV'          -ForegroundColor DarkCyan
Write-Host '  ==========================================' -ForegroundColor DarkCyan
Write-Host ''

$conn = New-Object System.Data.SqlClient.SqlConnection $polaczenie
try {
    $conn.Open()
} catch {
    Write-Host "   BLAD  Nie moge polaczyc sie z baza." -ForegroundColor Red
    Write-Host "         $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "         Sprawdz, czy LocalDB dziala:  sqllocaldb start MSSQLLocalDB" -ForegroundColor Yellow
    exit 1
}

# Lista tabel prosto ze schematu - nic nie trzeba wpisywac recznie.
$cmd = $conn.CreateCommand()
$cmd.CommandText = @"
SELECT TABLE_NAME
FROM INFORMATION_SCHEMA.TABLES
WHERE TABLE_TYPE = 'BASE TABLE'
ORDER BY TABLE_NAME
"@
$tabele = @()
$rd = $cmd.ExecuteReader()
while ($rd.Read()) { $tabele += $rd.GetString(0) }
$rd.Close()

if ($tabele.Count -eq 0) {
    Write-Host '   !    Baza nie zawiera zadnych tabel.' -ForegroundColor Yellow
    $conn.Close()
    exit 1
}

$top = if ($MaxWierszy -gt 0) { "TOP ($MaxWierszy) " } else { '' }
$suma = 0

foreach ($t in $tabele) {
    $c = $conn.CreateCommand()
    $c.CommandText = "SELECT $top* FROM [dbo].[$t]"
    $c.CommandTimeout = 120

    $dt = New-Object System.Data.DataTable
    $da = New-Object System.Data.SqlClient.SqlDataAdapter $c
    [void]$da.Fill($dt)

    $plik = Join-Path $wyjscie "$t.csv"
    $dt | Export-Csv -Path $plik -NoTypeInformation -Encoding UTF8

    $suma += $dt.Rows.Count
    $kolor = if ($dt.Rows.Count -eq 0) { 'DarkGray' } else { 'Green' }
    Write-Host ("   {0,-28} {1,6} wierszy" -f $t, $dt.Rows.Count) -ForegroundColor $kolor
}

$conn.Close()

Write-Host ''
Write-Host "   Gotowe: $($tabele.Count) tabel, $suma wierszy lacznie" -ForegroundColor Green
Write-Host "   Pliki:  $wyjscie" -ForegroundColor Cyan
Write-Host ''
