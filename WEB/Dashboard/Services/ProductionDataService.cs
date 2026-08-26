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
        public async Task<DailyKpi> GetDailyKpiAsync()
        {
            const string sql = @"
                SELECT
                    ISNULL(AVG(CAST(w.Wskaznik_OEE   AS FLOAT)), 0) AS OEE,
                    ISNULL(AVG(CAST(w.Dostepnosc      AS FLOAT)), 0) AS A,
                    ISNULL(AVG(CAST(w.Wydajnosc       AS FLOAT)), 0) AS P,
                    ISNULL(AVG(CAST(w.Jakosc          AS FLOAT)), 0) AS Q,
                    ISNULL(AVG(CAST(w.Wskaznik_FTY    AS FLOAT)), 0) AS FTY,
                    ISNULL(AVG(CAST(w.Czas_Cyklu_ms   AS FLOAT)), 0) AS AvgCykl,
                    ISNULL(SUM(CASE WHEN r.ID_Stanowiska = 4 THEN r.Ilosc_Wyprodukowanych - r.Liczba_Wadliwych ELSE 0 END), 0) AS Wyprod,
                    ISNULL(SUM(CASE WHEN r.ID_Stanowiska = 4 THEN r.Liczba_Wadliwych ELSE 0 END), 0) AS Wadliwe
                FROM [dbo].[Wskazniki] w
                JOIN [dbo].[Realizacja_Produkcji] r ON w.ID_Realizacji = r.ID
                WHERE CAST(w.DataCzas_Pomiaru AS DATE) = CAST(GETDATE() AS DATE)";

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
        // Status każdego stanowiska (ostatnia aktywność)
        // ─────────────────────────────────────────────────────────────
        public async Task<List<StanowiskoStatus>> GetStanowiskaStatusAsync()
        {
            const string sql = @"
                SELECT
                    s.ID_Stanowiska,
                    s.Nazwa_Stanowiska,
                    o.Imie_Nazwisko,
                    w.Wskaznik_OEE,
                    r.Czas_Cyklu_ms,
                    r.Czas_Zakonczenia,
                    r.Kod_Postoju
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
                LEFT JOIN [dbo].[Operator]   o ON r.ID_Operatora  = o.ID_Operatora
                LEFT JOIN [dbo].[Wskazniki]  w ON r.ID            = w.ID_Realizacji
                ORDER BY s.ID_Stanowiska";

            var result = new List<StanowiskoStatus>();
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                result.Add(new StanowiskoStatus
                {
                    IDStanowiska   = rdr.GetInt32(0),
                    Nazwa          = rdr.GetString(1),
                    ImieNazwisko   = rdr.IsDBNull(2) ? null : rdr.GetString(2),
                    OEE            = rdr.IsDBNull(3) ? null : (double?)Convert.ToDouble(rdr[3]),
                    OstatniCyklMs  = rdr.IsDBNull(4) ? null : rdr.GetInt32(4),
                    OstatniaCzas   = rdr.IsDBNull(5) ? null : rdr.GetDateTime(5),
                    KodPostoju     = rdr.IsDBNull(6) ? null : rdr.GetString(6)
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
        // Statystyki operatora
        // ─────────────────────────────────────────────────────────────
        public async Task<OperatorStats> GetOperatorStatsAsync(int idOperatora)
        {
            const string sql = @"
                SELECT
                    COUNT(*),
                    ISNULL(AVG(CAST(w.Wskaznik_OEE AS FLOAT)), 0),
                    ISNULL(AVG(CAST(w.Wskaznik_FTY AS FLOAT)), 0),
                    ISNULL(SUM(r.Ilosc_Wyprodukowanych), 0),
                    ISNULL(SUM(r.Liczba_Wadliwych), 0),
                    ISNULL(AVG(CAST(r.Czas_Cyklu_ms AS FLOAT)), 0)
                FROM [dbo].[Realizacja_Produkcji] r
                LEFT JOIN [dbo].[Wskazniki] w ON r.ID = w.ID_Realizacji
                WHERE r.ID_Operatora = @IDOp
                  AND CAST(r.Czas_Zakonczenia AS DATE) = CAST(GETDATE() AS DATE)";

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@IDOp", idOperatora);
            await using var rdr = await cmd.ExecuteReaderAsync();

            var stats = new OperatorStats();
            if (await rdr.ReadAsync())
            {
                stats.CykleDziś        = rdr.GetInt32(0);
                stats.OEEDziś          = rdr.GetDouble(1);
                stats.FTYDziś          = rdr.GetDouble(2);
                stats.WyprodukowanoDziś = rdr.GetInt32(3);
                stats.WadliweDziś      = rdr.GetInt32(4);
                stats.AvgCyklMs        = rdr.GetDouble(5);
            }
            await rdr.CloseAsync();

            stats.Trend = await GetOeeTrendForOperatorAsync(idOperatora, 20);
            return stats;
        }

        private async Task<List<OeeTrendPoint>> GetOeeTrendForOperatorAsync(int idOp, int n)
        {
            var sql = $@"
                SELECT TOP {n}
                    w.DataCzas_Pomiaru,
                    s.Nazwa_Stanowiska,
                    CAST(w.Wskaznik_OEE AS FLOAT),
                    CAST(w.Dostepnosc   AS FLOAT),
                    CAST(w.Wydajnosc    AS FLOAT),
                    CAST(w.Jakosc       AS FLOAT)
                FROM [dbo].[Wskazniki] w
                JOIN [dbo].[Stanowisko] s ON w.ID_Stanowiska = s.ID_Stanowiska
                JOIN [dbo].[Realizacja_Produkcji] r ON w.ID_Realizacji = r.ID
                WHERE r.ID_Operatora = @IDOp
                ORDER BY w.DataCzas_Pomiaru DESC";

            var result = new List<OeeTrendPoint>();
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@IDOp", idOp);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                result.Add(new OeeTrendPoint
                {
                    Czas       = rdr.GetDateTime(0),
                    Stanowisko = rdr.GetString(1),
                    OEE        = rdr.GetDouble(2),
                    Dostepnosc = rdr.GetDouble(3),
                    Wydajnosc  = rdr.GetDouble(4),
                    Jakosc     = rdr.GetDouble(5)
                });
            result.Reverse();
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

        // ─────────────────────────────────────────────────────────────
        // Harmonogram
        // ─────────────────────────────────────────────────────────────
        public async Task<List<HarmonogramRow>> GetHarmonogramDzisAsync()
        {
            const string sql = @"
                SELECT h.ID, zp.Nazwa_Zlecenia, s.Nazwa_Stanowiska,
                       o.Imie_Nazwisko, h.Czas_Rozpoczecia, h.Czas_Zakonczenia
                FROM [dbo].[Harmonogram] h
                JOIN [dbo].[Zlecenie_Produkcyjne] zp ON h.ID_Zlecenia   = zp.ID_Zlecenia
                JOIN [dbo].[Stanowisko]           s  ON h.ID_Stanowiska = s.ID_Stanowiska
                JOIN [dbo].[Operator]             o  ON h.ID_Operatora  = o.ID_Operatora
                WHERE CAST(ISNULL(h.Czas_Rozpoczecia, GETDATE()) AS DATE) = CAST(GETDATE() AS DATE)
                ORDER BY s.ID_Stanowiska";

            var result = new List<HarmonogramRow>();
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                result.Add(new HarmonogramRow
                {
                    ID            = rdr.GetInt32(0),
                    NazwaZlecenia = rdr.GetString(1),
                    Stanowisko    = rdr.GetString(2),
                    Operator      = rdr.GetString(3),
                    CzasRozp      = rdr.IsDBNull(4) ? null : rdr.GetDateTime(4),
                    CzasZak       = rdr.IsDBNull(5) ? null : rdr.GetDateTime(5)
                });
            return result;
        }

        public async Task AddHarmonogramAsync(int idZlecenia, int idStanowiska, int idOperatora)
        {
            const string sql = @"
                INSERT INTO [dbo].[Harmonogram] (ID_Zlecenia, ID_Stanowiska, ID_Operatora, Czas_Rozpoczecia)
                VALUES (@Zl, @Stan, @Op, GETDATE())";
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Zl",   idZlecenia);
            cmd.Parameters.AddWithValue("@Stan",  idStanowiska);
            cmd.Parameters.AddWithValue("@Op",    idOperatora);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
