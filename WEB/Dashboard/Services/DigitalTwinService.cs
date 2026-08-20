using LiniaProdukcyjnaDashboard.Models;
using Microsoft.Data.SqlClient;

namespace LiniaProdukcyjnaDashboard.Services
{
    /// <summary>
    /// Pobiera dane na potrzeby strony Digital Twin — status stanowisk,
    /// podsumowanie dnia, ostatnie cykle. Używa tych samych danych SQL
    /// co ProductionDataService, ale zwraca je w formacie pod wizualizację SVG.
    /// </summary>
    public class DigitalTwinService
    {
        private readonly string _cs;

        public DigitalTwinService(IConfiguration cfg)
        {
            _cs = cfg.GetConnectionString("BazaDanychRB")!;
        }

        // Status każdego stanowiska z danymi na żywo
        public async Task<List<DigitalTwinStanowiskoVM>> GetStanowiskaLiveAsync()
        {
            const string sql = @"
                SELECT
                    s.ID_Stanowiska,
                    s.Nazwa_Stanowiska,
                    o.Imie_Nazwisko           AS Operator,
                    z.Nazwa_Zlecenia          AS AktywneZlecenie,
                    w.Wskaznik_OEE            AS OEE,
                    w.Wskaznik_FTY            AS FTY,
                    r.Czas_Cyklu_ms           AS CzasCykluMs,
                    z.Czas_Planowany_ms       AS CzasPlanowanyMs,
                    r.Czas_Zakonczenia        AS OstatniaCzas,
                    r.Kod_Postoju             AS KodPostoju,
                    ISNULL((
                        SELECT SUM(r2.Ilosc_Wyprodukowanych)
                        FROM [dbo].[Realizacja_Produkcji] r2
                        WHERE r2.ID_Stanowiska = s.ID_Stanowiska
                          AND CAST(r2.Czas_Zakonczenia AS DATE) = CAST(GETDATE() AS DATE)
                    ), 0) AS SztukDzisiaj,
                    ISNULL((
                        SELECT SUM(r3.Liczba_Wadliwych)
                        FROM [dbo].[Realizacja_Produkcji] r3
                        WHERE r3.ID_Stanowiska = s.ID_Stanowiska
                          AND CAST(r3.Czas_Zakonczenia AS DATE) = CAST(GETDATE() AS DATE)
                    ), 0) AS WadliweDzisiaj
                FROM [dbo].[Stanowisko] s
                LEFT JOIN (
                    SELECT *,
                           ROW_NUMBER() OVER(PARTITION BY ID_Stanowiska ORDER BY Czas_Zakonczenia DESC, ID DESC) AS rn
                    FROM [dbo].[Realizacja_Produkcji]
                ) r ON s.ID_Stanowiska = r.ID_Stanowiska AND r.rn = 1
                LEFT JOIN [dbo].[Operator]              o ON r.ID_Operatora  = o.ID_Operatora
                LEFT JOIN [dbo].[Zlecenie_Produkcyjne]  z ON r.ID_Zlecenia   = z.ID_Zlecenia
                LEFT JOIN [dbo].[Wskazniki]             w ON r.ID            = w.ID_Realizacji
                ORDER BY s.ID_Stanowiska";

            var result = new List<DigitalTwinStanowiskoVM>();
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();

            while (await rdr.ReadAsync())
            {
                result.Add(new DigitalTwinStanowiskoVM
                {
                    IDStanowiska    = rdr.GetInt32(0),
                    Nazwa           = rdr.GetString(1),
                    Operator        = rdr.IsDBNull(2)  ? null : rdr.GetString(2),
                    AktywneZlecenie = rdr.IsDBNull(3)  ? null : rdr.GetString(3),
                    OEE             = rdr.IsDBNull(4)  ? null : (double?)Convert.ToDouble(rdr.GetValue(4)),
                    FTY             = rdr.IsDBNull(5)  ? null : (double?)Convert.ToDouble(rdr.GetValue(5)),
                    CzasCykluMs     = rdr.IsDBNull(6)  ? null : rdr.GetInt32(6),
                    CzasPlanowanyMs = rdr.IsDBNull(7)  ? null : rdr.GetInt32(7),
                    OstatniaCzas    = rdr.IsDBNull(8)  ? null : rdr.GetDateTime(8),
                    KodPostoju      = rdr.IsDBNull(9)  ? null : rdr.GetString(9),
                    SztukDzisiaj    = rdr.GetInt32(10),
                    WadliweDzisiaj  = rdr.GetInt32(11),
                });
            }
            return result;
        }

        // Podsumowanie dnia
        public async Task<DigitalTwinSummaryVM> GetDzisiejszeSummaryAsync()
        {
            const string sql = @"
                SELECT
                    ISNULL(SUM(r.Ilosc_Wyprodukowanych), 0)  AS SztukDzisiaj,
                    ISNULL(SUM(r.Liczba_Wadliwych), 0)        AS WadliweDzisiaj,
                    ISNULL(AVG(CAST(w.Wskaznik_OEE AS FLOAT)), 0) AS OEEDzisiaj,
                    (SELECT TOP 1 z2.Nazwa_Zlecenia
                     FROM [dbo].[Realizacja_Produkcji] r2
                     JOIN [dbo].[Zlecenie_Produkcyjne] z2 ON r2.ID_Zlecenia = z2.ID_Zlecenia
                     WHERE CAST(r2.Czas_Zakonczenia AS DATE) = CAST(GETDATE() AS DATE)
                     ORDER BY r2.Czas_Zakonczenia DESC) AS AktywneZlecenie,
                    (SELECT COUNT(*)
                     FROM [dbo].[Realizacja_Produkcji]
                     WHERE Czas_Zakonczenia >= DATEADD(HOUR, -1, GETDATE())) AS CykleGodzina
                FROM [dbo].[Realizacja_Produkcji] r
                LEFT JOIN [dbo].[Wskazniki] w ON r.ID = w.ID_Realizacji
                WHERE CAST(r.Czas_Zakonczenia AS DATE) = CAST(GETDATE() AS DATE)";

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();

            if (!await rdr.ReadAsync()) return new DigitalTwinSummaryVM();
            return new DigitalTwinSummaryVM
            {
                SztukDzisiaj    = rdr.GetInt32(0),
                WadliweDzisiaj  = rdr.GetInt32(1),
                OEEDzisiaj      = rdr.GetDouble(2),
                AktywneZlecenie = rdr.IsDBNull(3) ? null : rdr.GetString(3),
                CykleGodzina    = rdr.GetInt32(4),
            };
        }

        // Ostatnie N cykli danego stanowiska (panel boczny)
        public async Task<List<RealizacjaRow>> GetOstatnieCykleAsync(int stanowiskoId, int top = 10)
        {
            const string sql = @"
                SELECT TOP (@top)
                    r.ID,
                    r.Czas_Zakonczenia,
                    s.Nazwa_Stanowiska,
                    r.ID_Stanowiska,
                    o.Imie_Nazwisko,
                    z.Nazwa_Zlecenia,
                    r.Czas_Cyklu_ms,
                    ISNULL(CAST(w.Wskaznik_OEE AS FLOAT), 0),
                    r.Wynik_QC,
                    r.Liczba_Wadliwych,
                    r.Kod_Postoju
                FROM [dbo].[Realizacja_Produkcji] r
                JOIN [dbo].[Stanowisko]             s ON r.ID_Stanowiska = s.ID_Stanowiska
                JOIN [dbo].[Operator]               o ON r.ID_Operatora  = o.ID_Operatora
                JOIN [dbo].[Zlecenie_Produkcyjne]   z ON r.ID_Zlecenia   = z.ID_Zlecenia
                LEFT JOIN [dbo].[Wskazniki]         w ON r.ID            = w.ID_Realizacji
                WHERE r.ID_Stanowiska = @stanId
                ORDER BY r.Czas_Zakonczenia DESC, r.ID DESC";

            var result = new List<RealizacjaRow>();
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@top",    top);
            cmd.Parameters.AddWithValue("@stanId", stanowiskoId);
            await using var rdr = await cmd.ExecuteReaderAsync();

            while (await rdr.ReadAsync())
            {
                result.Add(new RealizacjaRow
                {
                    ID              = rdr.GetInt32(0),
                    Czas            = rdr.GetDateTime(1),
                    Stanowisko      = rdr.GetString(2),
                    IDStanowiska    = rdr.GetInt32(3),
                    Operator        = rdr.GetString(4),
                    Zlecenie        = rdr.GetString(5),
                    CyklMs          = rdr.GetInt32(6),
                    OEE             = rdr.GetDouble(7),
                    WynikQC         = rdr.GetBoolean(8),
                    LiczbaWadliwych = rdr.GetInt32(9),
                    KodPostoju      = rdr.IsDBNull(10) ? null : rdr.GetString(10),
                });
            }
            return result;
        }
    }
}
