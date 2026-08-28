using LiniaProdukcyjnaDashboard.Models;
using Microsoft.Data.SqlClient;

namespace LiniaProdukcyjnaDashboard.Services
{
    public class ProductionDataService
    {
        private readonly string _cs;

        public ProductionDataService(IConfiguration cfg)
        {
            _cs = cfg.GetConnectionString("BazaDanychRB")!;
        }

        // ─────────────────────────────────────────────────────────────
        // KPI dzienne
        // ─────────────────────────────────────────────────────────────
        public async Task<DailyKpi> GetDailyKpiAsync(int oknoSztuk = 50)
        {
            var sql = $@"
                ;WITH Ostatnie AS (
                    SELECT TOP {oknoSztuk} w.*, r.Ilosc_Wyprodukowanych, r.Liczba_Wadliwych
                    FROM [dbo].[Wskazniki] w
                    JOIN [dbo].[Realizacja_Produkcji] r ON w.ID_Realizacji = r.ID
                    ORDER BY w.DataCzas_Pomiaru DESC
                )
                SELECT
                    ISNULL(AVG(CAST(Wskaznik_OEE   AS FLOAT)), 0) AS OEE,
                    ISNULL(AVG(CAST(Dostepnosc      AS FLOAT)), 0) AS A,
                    ISNULL(AVG(CAST(Wydajnosc       AS FLOAT)), 0) AS P,
                    ISNULL(AVG(CAST(Jakosc          AS FLOAT)), 0) AS Q,
                    ISNULL(AVG(CAST(Wskaznik_FTY    AS FLOAT)), 0) AS FTY,
                    ISNULL(AVG(CAST(Czas_Cyklu_ms   AS FLOAT)), 0) AS AvgCykl,
                    ISNULL(SUM(CASE WHEN ID_Stanowiska = 4 THEN Ilosc_Wyprodukowanych - Liczba_Wadliwych ELSE 0 END), 0) AS Wyprod,
                    ISNULL(SUM(CASE WHEN ID_Stanowiska = 4 THEN Liczba_Wadliwych ELSE 0 END), 0) AS Wadliwe
                FROM Ostatnie";

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();

            if (!await rdr.ReadAsync()) return new DailyKpi();
            return new DailyKpi
            {
                OEE              = rdr.GetDouble(0),
                Dostepnosc       = rdr.GetDouble(1),
                Wydajnosc        = rdr.GetDouble(2),
                Jakosc           = rdr.GetDouble(3),
                FTY              = rdr.GetDouble(4),
                AvgCyklMs        = rdr.GetDouble(5),
                Wyprodukowano    = rdr.GetInt32(6),
                LiczbaWadliwych  = rdr.GetInt32(7)
            };
        }

        // ─────────────────────────────────────────────────────────────
        // Wyprodukowano ogolem (od zawsze, bez okna) - kafelek "Wyprodukowano ogolnie"
        // ─────────────────────────────────────────────────────────────
        public async Task<int> GetTotalProducedAllTimeAsync()
        {
            const string sql = @"
                SELECT ISNULL(SUM(CASE WHEN ID_Stanowiska = 4 THEN Ilosc_Wyprodukowanych - Liczba_Wadliwych ELSE 0 END), 0)
                FROM [dbo].[Realizacja_Produkcji]";

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        // ─────────────────────────────────────────────────────────────
        // Wydajnosc cyklu per wyrob = suma czasow zadanych / suma czasow rzeczywistych
        // (wszystkie stanowiska razem). LEFT JOIN, zeby ZAWSZE zwrocic komplet wyrobow,
        // takze te bez zarejestrowanej produkcji.
        // ─────────────────────────────────────────────────────────────
        public async Task<List<WyrobCzasCyklu>> GetAvgCycleTimePerWyrobAsync()
        {
            const string sql = @"
                SELECT w.Nazwa_Wyrobu,
                       ISNULL(SUM(CAST(r.Czas_Cyklu_ms AS FLOAT)), 0)     AS SumaRzeczywista,
                       ISNULL(SUM(CAST(pm.Czas_Jednostkowy AS FLOAT)), 0) AS SumaZadana
                FROM [dbo].[Wyrob] w
                LEFT JOIN [dbo].[Zlecenie_Produkcyjne] zp ON zp.ID_Wyrobu  = w.ID_Wyrobu
                LEFT JOIN [dbo].[Realizacja_Produkcji] r  ON r.ID_Zlecenia = zp.ID_Zlecenia
                LEFT JOIN [dbo].[Proces_Montazu]       pm ON pm.ID_Wyrobu  = w.ID_Wyrobu
                                                          AND pm.ID_Stanowiska = r.ID_Stanowiska
                GROUP BY w.ID_Wyrobu, w.Nazwa_Wyrobu
                ORDER BY w.ID_Wyrobu";

            var result = new List<WyrobCzasCyklu>();
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                double rzeczywista = rdr.GetDouble(1);
                double zadana      = rdr.GetDouble(2);
                result.Add(new WyrobCzasCyklu
                {
                    Nazwa      = rdr.GetString(0),
                    SumaCzasMs = rzeczywista,
                    Wydajnosc  = rzeczywista > 0 ? zadana / rzeczywista : (double?)null
                });
            }
            return result;
        }

        // ─────────────────────────────────────────────────────────────
        // Popularnosc wyrobow (% udzialu w ukonczonych sztukach - QC, stanowisko 4)
        // ─────────────────────────────────────────────────────────────
        public async Task<List<WyrobPopularnosc>> GetWyrobPopularnosciAsync()
        {
            const string sql = @"
                SELECT w.Nazwa_Wyrobu, COUNT(*) AS Ilosc
                FROM [dbo].[Realizacja_Produkcji] r
                JOIN [dbo].[Zlecenie_Produkcyjne]  zp ON r.ID_Zlecenia = zp.ID_Zlecenia
                JOIN [dbo].[Wyrob]                 w  ON zp.ID_Wyrobu = w.ID_Wyrobu
                WHERE r.ID_Stanowiska = 4
                GROUP BY w.Nazwa_Wyrobu
                ORDER BY COUNT(*) DESC";

            var raw = new List<(string nazwa, int ilosc)>();
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                raw.Add((rdr.GetString(0), rdr.GetInt32(1)));

            int total = raw.Sum(r => r.ilosc);
            return raw.Select(r => new WyrobPopularnosc
            {
                Nazwa   = r.nazwa,
                Ilosc   = r.ilosc,
                Procent = total > 0 ? (double)r.ilosc / total : 0
            }).ToList();
        }

        // ─────────────────────────────────────────────────────────────
        // Status każdego stanowiska (ostatnia aktywność)
        // Wydajnosc = suma czasow zadanych (Proces_Montazu wg wyrobu danej sztuki)
        //             / suma czasow rzeczywistych (Czas_Cyklu_ms), z ostatnich 3 sztuk.
        // ─────────────────────────────────────────────────────────────
        public async Task<List<StanowiskoStatus>> GetStanowiskaStatusAsync()
        {
            const string sql = @"
                SELECT
                    s.ID_Stanowiska,
                    s.Nazwa_Stanowiska,
                    w.Wskaznik_OEE,
                    r.Czas_Cyklu_ms,
                    r.Czas_Zakonczenia,
                    r.Kod_Postoju,
                    zp.Nazwa_Zlecenia,
                    wy.Nazwa_Wyrobu,
                    pmLast.Czas_Jednostkowy,
                    perf.SumaZadana,
                    perf.SumaRzeczywista
                FROM [dbo].[Stanowisko] s
                LEFT JOIN (
                    SELECT r1.*
                    FROM [dbo].[Realizacja_Produkcji] r1
                    INNER JOIN (
                        SELECT ID_Stanowiska, MAX(Czas_Zakonczenia) AS Maks
                        FROM [dbo].[Realizacja_Produkcji]
                        GROUP BY ID_Stanowiska
                    ) r2 ON r1.ID_Stanowiska = r2.ID_Stanowiska
                         AND r1.Czas_Zakonczenia = r2.Maks
                ) r ON s.ID_Stanowiska = r.ID_Stanowiska
                LEFT JOIN [dbo].[Wskazniki]           w      ON r.ID            = w.ID_Realizacji
                LEFT JOIN [dbo].[Zlecenie_Produkcyjne] zp    ON r.ID_Zlecenia   = zp.ID_Zlecenia
                LEFT JOIN [dbo].[Wyrob]                wy    ON zp.ID_Wyrobu    = wy.ID_Wyrobu
                LEFT JOIN [dbo].[Proces_Montazu]       pmLast ON pmLast.ID_Wyrobu = zp.ID_Wyrobu
                                                              AND pmLast.ID_Stanowiska = s.ID_Stanowiska
                OUTER APPLY (
                    SELECT SUM(pm.Czas_Jednostkowy) AS SumaZadana,
                           SUM(ost3.Czas_Cyklu_ms)   AS SumaRzeczywista
                    FROM (
                        SELECT TOP 3 rp.Czas_Cyklu_ms, rp.ID_Zlecenia
                        FROM [dbo].[Realizacja_Produkcji] rp
                        WHERE rp.ID_Stanowiska = s.ID_Stanowiska
                        ORDER BY rp.Czas_Zakonczenia DESC
                    ) ost3
                    JOIN [dbo].[Zlecenie_Produkcyjne] zp2 ON zp2.ID_Zlecenia = ost3.ID_Zlecenia
                    JOIN [dbo].[Proces_Montazu]       pm  ON pm.ID_Wyrobu = zp2.ID_Wyrobu
                                                          AND pm.ID_Stanowiska = s.ID_Stanowiska
                ) perf
                ORDER BY s.ID_Stanowiska";

            var result = new List<StanowiskoStatus>();
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                double? sumaZadana       = rdr.IsDBNull(9)  ? null : Convert.ToDouble(rdr[9]);
                double? sumaRzeczywista  = rdr.IsDBNull(10) ? null : Convert.ToDouble(rdr[10]);

                result.Add(new StanowiskoStatus
                {
                    IDStanowiska   = rdr.GetInt32(0),
                    Nazwa          = rdr.GetString(1),
                    OEE            = rdr.IsDBNull(2) ? null : (double?)Convert.ToDouble(rdr[2]),
                    OstatniCyklMs  = rdr.IsDBNull(3) ? null : rdr.GetInt32(3),
                    OstatniaCzas   = rdr.IsDBNull(4) ? null : rdr.GetDateTime(4),
                    KodPostoju     = rdr.IsDBNull(5) ? null : rdr.GetString(5),
                    NazwaZlecenia  = rdr.IsDBNull(6) ? null : rdr.GetString(6),
                    NazwaWyrobu    = rdr.IsDBNull(7) ? null : rdr.GetString(7),
                    OstatniCzasZadanyMs = rdr.IsDBNull(8) ? null : (int?)Convert.ToInt32(rdr[8]),
                    Wydajnosc      = (sumaZadana.HasValue && sumaRzeczywista is > 0)
                                        ? sumaZadana.Value / sumaRzeczywista.Value
                                        : null
                });
            }
            return result;
        }

        // ─────────────────────────────────────────────────────────────
        // Trend OEE — ostatnie N cykli (wszystkie stanowiska)
        // ─────────────────────────────────────────────────────────────
        public async Task<List<OeeTrendPoint>> GetOeeTrendAsync(int n = 30)
        {
            var sql = $@"
                SELECT TOP {n}
                    w.DataCzas_Pomiaru,
                    s.Nazwa_Stanowiska,
                    CAST(w.Wskaznik_OEE  AS FLOAT),
                    CAST(w.Dostepnosc    AS FLOAT),
                    CAST(w.Wydajnosc     AS FLOAT),
                    CAST(w.Jakosc        AS FLOAT)
                FROM [dbo].[Wskazniki] w
                JOIN [dbo].[Stanowisko] s ON w.ID_Stanowiska = s.ID_Stanowiska
                ORDER BY w.DataCzas_Pomiaru DESC";

            var result = new List<OeeTrendPoint>();
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                result.Add(new OeeTrendPoint
                {
                    Czas       = rdr.GetDateTime(0),
                    Stanowisko = rdr.GetString(1),
                    OEE        = rdr.GetDouble(2),
                    Dostepnosc = rdr.GetDouble(3),
                    Wydajnosc  = rdr.GetDouble(4),
                    Jakosc     = rdr.GetDouble(5)
                });
            }
            result.Reverse();
            return result;
        }

        // ─────────────────────────────────────────────────────────────
        // Przyczyny postojów (dziś)
        // ─────────────────────────────────────────────────────────────
        public async Task<List<PostojCause>> GetPostojeCausesAsync()
        {
            const string sql = @"
                SELECT ISNULL(Kod_Postoju, 'Nieznany') AS Kod,
                       COUNT(*) AS Liczba
                FROM [dbo].[Realizacja_Produkcji]
                WHERE Kod_Postoju IS NOT NULL
                  AND CAST(Czas_Zakonczenia AS DATE) = CAST(GETDATE() AS DATE)
                GROUP BY Kod_Postoju
                ORDER BY Liczba DESC";

            var result = new List<PostojCause>();
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                result.Add(new PostojCause { Kod = rdr.GetString(0), Liczba = rdr.GetInt32(1) });
            return result;
        }

        // ─────────────────────────────────────────────────────────────
        // Ostatnie realizacje (tabela)
        // ─────────────────────────────────────────────────────────────
        public async Task<List<RealizacjaRow>> GetLatestRealizacjeAsync(int n = 20, int? idOperatora = null)
        {
            var sql = $@"
                SELECT TOP {n}
                    r.ID, r.Czas_Zakonczenia,
                    s.Nazwa_Stanowiska,
                    o.Imie_Nazwisko,
                    zp.Nazwa_Zlecenia,
                    r.Czas_Cyklu_ms,
                    ISNULL(CAST(w.Wskaznik_OEE AS FLOAT), 0),
                    r.Wynik_QC,
                    r.Liczba_Wadliwych,
                    r.Kod_Postoju
                FROM [dbo].[Realizacja_Produkcji] r
                JOIN [dbo].[Stanowisko]           s  ON r.ID_Stanowiska = s.ID_Stanowiska
                JOIN [dbo].[Operator]             o  ON r.ID_Operatora  = o.ID_Operatora
                JOIN [dbo].[Zlecenie_Produkcyjne] zp ON r.ID_Zlecenia   = zp.ID_Zlecenia
                LEFT JOIN [dbo].[Wskazniki]       w  ON r.ID            = w.ID_Realizacji
                {(idOperatora.HasValue ? "WHERE r.ID_Operatora = @IDOp" : "")}
                ORDER BY r.Czas_Zakonczenia DESC";

            var result = new List<RealizacjaRow>();
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            if (idOperatora.HasValue) cmd.Parameters.AddWithValue("@IDOp", idOperatora.Value);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                result.Add(new RealizacjaRow
                {
                    ID              = rdr.GetInt32(0),
                    Czas            = rdr.GetDateTime(1),
                    Stanowisko      = rdr.GetString(2),
                    Operator        = rdr.GetString(3),
                    Zlecenie        = rdr.GetString(4),
                    CyklMs          = rdr.GetInt32(5),
                    OEE             = rdr.GetDouble(6),
                    WynikQC         = rdr.GetBoolean(7),
                    LiczbaWadliwych = rdr.GetInt32(8),
                    KodPostoju      = rdr.IsDBNull(9) ? null : rdr.GetString(9)
                });
            }
            return result;
        }

        // ─────────────────────────────────────────────────────────────
        // Zlecenia
        // ─────────────────────────────────────────────────────────────
        public async Task<List<ZlecenieVM>> GetZleceniaAsync()
        {
            const string sql = @"
                SELECT zp.ID_Zlecenia, zp.Nazwa_Zlecenia, zp.Ilosc_Sztuk,
                       (SELECT ISNULL(SUM(Ilosc_Wyprodukowanych - Liczba_Wadliwych), 0) 
                     FROM [dbo].[Realizacja_Produkcji] 
                     WHERE ID_Zlecenia = zp.ID_Zlecenia AND ID_Stanowiska = 4) AS Wyprodukowano,
                       zp.Data_Realizacji, zp.Status_Zlecenia,
                       ISNULL(zp.Czas_Planowany_ms, 0),
                       w.Nazwa_Wyrobu
                FROM [dbo].[Zlecenie_Produkcyjne] zp
                LEFT JOIN [dbo].[Wyrob] w ON zp.ID_Wyrobu = w.ID_Wyrobu
                GROUP BY zp.ID_Zlecenia, zp.Nazwa_Zlecenia, zp.Ilosc_Sztuk,
                         zp.Data_Realizacji, zp.Status_Zlecenia,
                         zp.Czas_Planowany_ms, w.Nazwa_Wyrobu
                ORDER BY zp.ID_Zlecenia DESC";

            var result = new List<ZlecenieVM>();
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                result.Add(new ZlecenieVM
                {
                    IDZlecenia      = rdr.GetInt32(0),
                    NazwaZlecenia   = rdr.GetString(1),
                    IloscSztuk      = rdr.GetInt32(2),
                    Wyprodukowano   = rdr.GetInt32(3),
                    DataRealizacji  = rdr.IsDBNull(4) ? null : rdr.GetDateTime(4),
                    StatusZlecenia  = rdr.GetString(5),
                    CzasPlanowanyMs = rdr.GetInt32(6),
                    NazwaWyrobu     = rdr.IsDBNull(7) ? null : rdr.GetString(7)
                });
            return result;
        }

        public async Task CreateZlecenieAsync(string nazwa, int iloscSztuk, int? idWyrobu,
                                              DateTime? dataRealizacji, int czasPlanMs)
        {
            const string sql = @"
                INSERT INTO [dbo].[Zlecenie_Produkcyjne]
                    (Nazwa_Zlecenia, Ilosc_Sztuk, ID_Wyrobu, Data_Realizacji,
                     Status_Zlecenia, Czas_Planowany_ms)
                VALUES (@Nazwa, @Ilosc, @Wyrob, @Data, 'Nowe', @Czas)";

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Nazwa", nazwa);
            cmd.Parameters.AddWithValue("@Ilosc", iloscSztuk);
            cmd.Parameters.AddWithValue("@Wyrob", (object?)idWyrobu ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Data",  (object?)dataRealizacji ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Czas",  czasPlanMs);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task UpdateStatusZlecenieAsync(int id, string status)
        {
            const string sql = "UPDATE [dbo].[Zlecenie_Produkcyjne] SET Status_Zlecenia=@S WHERE ID_Zlecenia=@ID";
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@S", status);
            cmd.Parameters.AddWithValue("@ID", id);
            await cmd.ExecuteNonQueryAsync();
        }

        // ─────────────────────────────────────────────────────────────
        // Listy wyboru (do formularzy)
        // ─────────────────────────────────────────────────────────────
        public async Task<List<SelectItem>> GetWyrobyAsync()
        {
            const string sql = "SELECT ID_Wyrobu, Nazwa_Wyrobu FROM [dbo].[Wyrob] ORDER BY Nazwa_Wyrobu";
            return await LoadSelectItemsAsync(sql);
        }

        public async Task<List<SelectItem>> GetOperatoryAsync()
        {
            const string sql = "SELECT ID_Operatora, Imie_Nazwisko FROM [dbo].[Operator] ORDER BY Imie_Nazwisko";
            return await LoadSelectItemsAsync(sql);
        }

        public async Task<List<SelectItem>> GetStanowiskaAsync()
        {
            const string sql = "SELECT ID_Stanowiska, Nazwa_Stanowiska FROM [dbo].[Stanowisko] ORDER BY ID_Stanowiska";
            return await LoadSelectItemsAsync(sql);
        }

        private async Task<List<SelectItem>> LoadSelectItemsAsync(string sql)
        {
            var result = new List<SelectItem>();
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                result.Add(new SelectItem { ID = rdr.GetInt32(0), Nazwa = rdr.GetString(1) });
            return result;
        }

    }
}
