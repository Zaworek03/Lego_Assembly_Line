using LiniaProdukcyjnaDashboard.Models;
using Microsoft.Data.SqlClient;

namespace LiniaProdukcyjnaDashboard.Services
{
    /// <summary>
    /// Serwis zleceń produkcyjnych: tworzenie, edycja, soft-delete, zmiana statusu,
    /// eksplozja BOM, backward scheduling, preempcja priorytetów.
    /// </summary>
    public class OrderService
    {
        private readonly string _cs;
        private readonly InventoryService _inv;
        private readonly ILogger<OrderService> _logger;

        public OrderService(IConfiguration cfg, InventoryService inv, ILogger<OrderService> logger)
        {
            _cs    = cfg.GetConnectionString("BazaDanychRB")!;
            _inv   = inv;
            _logger = logger;
        }

        // ── Wyroby (do dropdownów) ───────────────────────────────────────
        public async Task<List<SelectItem>> GetWyrobyAsync()
        {
            const string sql = "SELECT ID_Wyrobu, Nazwa_Wyrobu FROM Wyrob ORDER BY Nazwa_Wyrobu";
            var result = new List<SelectItem>();
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                result.Add(new SelectItem { ID = rdr.GetInt32(0), Nazwa = rdr.GetString(1) });
            return result;
        }

        // ── Raport zleceń ────────────────────────────────────────────────
        public async Task<List<ZlecenieRaportRow>> GetRaportAsync(DateTime? dataOd = null, DateTime? dataDo = null)
        {
            var od = dataOd ?? DateTime.Today.AddDays(-30);
            var _do = dataDo ?? DateTime.Today.AddDays(1);

            const string sql = @"
                SELECT
                    zp.ID_Zlecenia,
                    zp.Nazwa_Zlecenia,
                    ISNULL(w.Nazwa_Wyrobu, N'—'),
                    zp.Ilosc_Sztuk,
                    ISNULL(zp.SztukOK, 0),
                    ISNULL(zp.SztukNOK, 0),
                    ISNULL(zp.Status_Zlecenia, 'Nowe'),
                    ISNULL(zp.Priorytet, 'Standardowy'),
                    zp.CreatedAt,
                    zp.StartedAt,
                    zp.CompletedAt,
                    zp.DueTime,
                    ISNULL(DATEDIFF(ms, zp.StartedAt, ISNULL(zp.CompletedAt, GETDATE())), 0),
                    -- Jakość (Q)
                    CASE WHEN (ISNULL(zp.SztukOK,0) + ISNULL(zp.SztukNOK,0)) > 0
                         THEN CAST(ISNULL(zp.SztukOK,0) AS float)
                              / (ISNULL(zp.SztukOK,0) + ISNULL(zp.SztukNOK,0))
                         ELSE 0 END,
                    -- Dostępność (A) = Avg(CyklMs / SplywMs) per zlecenie
                    ISNULL((SELECT
                        CASE WHEN SUM(CAST(rp.Czas_Splywu_ms AS bigint)) > 0
                             THEN CAST(SUM(CAST(rp.Czas_Cyklu_ms AS bigint)) AS float)
                                  / SUM(CAST(rp.Czas_Splywu_ms AS bigint))
                             ELSE 0 END
                        FROM dbo.Realizacja_Produkcji rp
                        WHERE rp.ID_Zlecenia = zp.ID_Zlecenia
                          AND rp.Czas_Splywu_ms > 0), 0),
                    -- Wydajność (P) = Avg(planowany_cykl / faktyczny_cykl) — uproszczone
                    ISNULL((SELECT
                        CASE WHEN SUM(CAST(rp.Czas_Cyklu_ms AS bigint)) > 0
                                  AND ISNULL(zp.Czas_Planowany_ms, 0) > 0
                             THEN CAST(COUNT(*) * ISNULL(zp.Czas_Planowany_ms, 0) AS float)
                                  / SUM(CAST(rp.Czas_Cyklu_ms AS bigint))
                             ELSE 0 END
                        FROM dbo.Realizacja_Produkcji rp
                        WHERE rp.ID_Zlecenia = zp.ID_Zlecenia
                          AND rp.Czas_Cyklu_ms > 0), 0)
                FROM dbo.Zlecenie_Produkcyjne zp
                LEFT JOIN dbo.Wyrob w ON zp.ID_Wyrobu = w.ID_Wyrobu
                WHERE zp.IsDeleted = 0
                  AND zp.CreatedAt >= @Od
                  AND zp.CreatedAt <  @Do
                ORDER BY zp.CreatedAt DESC";

            var result = new List<ZlecenieRaportRow>();
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Od", od);
            cmd.Parameters.AddWithValue("@Do", _do);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                result.Add(new ZlecenieRaportRow
                {
                    IDZlecenia    = rdr.GetInt32(0),
                    NazwaZlecenia = rdr.GetString(1),
                    NazwaWyrobu   = rdr.GetString(2),
                    IloscSztuk    = rdr.GetInt32(3),
                    SztukOK       = rdr.GetInt32(4),
                    SztukNOK      = rdr.GetInt32(5),
                    Status        = rdr.GetString(6),
                    Priorytet     = rdr.GetString(7),
                    CreatedAt     = rdr.IsDBNull(8)  ? null : rdr.GetDateTime(8),
                    StartedAt     = rdr.IsDBNull(9)  ? null : rdr.GetDateTime(9),
                    CompletedAt   = rdr.IsDBNull(10) ? null : rdr.GetDateTime(10),
                    DueTime       = rdr.IsDBNull(11) ? null : rdr.GetDateTime(11),
                    CzasTrwaniaMs = rdr.GetInt64(12),
                    Jakosc        = Math.Min(1.0, rdr.GetDouble(13)),
                    Dostepnosc    = Math.Min(1.0, rdr.GetDouble(14)),
                    Wydajnosc     = Math.Min(1.0, rdr.GetDouble(15))
                });
            }
            return result;
        }

        // ── Pobieranie zleceń ────────────────────────────────────────────
        public async Task<List<ZlecenieVM>> GetZleceniaAsync()
        {
            const string sql = @"
                SELECT zp.ID_Zlecenia, zp.Nazwa_Zlecenia, zp.Ilosc_Sztuk,
                       zp.SztukOK,
                       zp.Data_Realizacji, zp.DueTime, zp.Status_Zlecenia,
                       ISNULL(zp.Czas_Planowany_ms, 0),
                       w.Nazwa_Wyrobu, zp.ID_Wyrobu,
                       zp.Priorytet, zp.NajpozniejszyStart,
                       zp.CompletedAt, zp.SztukNOK
                FROM [dbo].[Zlecenie_Produkcyjne] zp
                LEFT JOIN [dbo].[Wyrob] w ON zp.ID_Wyrobu = w.ID_Wyrobu
                WHERE zp.IsDeleted = 0
                ORDER BY zp.PriorytetNum DESC, zp.DueTime ASC, zp.ID_Zlecenia DESC";

            var result = new List<ZlecenieVM>();
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                result.Add(MapZlecenieVM(rdr));
            return result;
        }

        // ── Pobieranie szczegółów jednego zlecenia ────────────────────────
        public async Task<ZlecenieDetail?> GetZlecenieDetailAsync(int id)
        {
            const string sql = @"
                SELECT zp.ID_Zlecenia, zp.Nazwa_Zlecenia, zp.Ilosc_Sztuk,
                       zp.SztukOK,
                       zp.Data_Realizacji, zp.DueTime, zp.Status_Zlecenia,
                       ISNULL(zp.Czas_Planowany_ms, 0),
                       w.Nazwa_Wyrobu, zp.ID_Wyrobu,
                       zp.Priorytet, zp.NajpozniejszyStart,
                       zp.CompletedAt, zp.SztukNOK
                FROM [dbo].[Zlecenie_Produkcyjne] zp
                LEFT JOIN [dbo].[Wyrob] w ON zp.ID_Wyrobu = w.ID_Wyrobu
                WHERE zp.ID_Zlecenia = @ID AND zp.IsDeleted = 0";

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ID", id);
            await using var rdr = await cmd.ExecuteReaderAsync();
            if (!await rdr.ReadAsync()) return null;
            var vm = MapZlecenieVM(rdr);
            await rdr.CloseAsync();

            var detail = new ZlecenieDetail
            {
                IDZlecenia        = vm.IDZlecenia,        NazwaZlecenia     = vm.NazwaZlecenia,
                IloscSztuk        = vm.IloscSztuk,        Wyprodukowano     = vm.SztukOK,
                DueTime           = vm.DueTime,           StatusZlecenia    = vm.StatusZlecenia,
                CzasPlanowanyMs   = vm.CzasPlanowanyMs,  NazwaWyrobu       = vm.NazwaWyrobu,
                IDWyrobu          = vm.IDWyrobu,          Priorytet         = vm.Priorytet,
                NajpozniejszyStart = vm.NajpozniejszyStart, CompletedAt      = vm.CompletedAt,
                SztukOK           = vm.SztukOK,           SztukNOK         = vm.SztukNOK
            };

            // Załaduj materiały (wynik eksplozji BOM)
            const string matSql = @"
                SELECT zm.ID_Materialu, m.Nazwa_Materialu, ISNULL(m.Wymiary,''),
                       ISNULL(m.TypWysokosci,''), ISNULL(m.Kolor,''),
                       zm.IloscWymagana, zm.IloscZarezerwowana, zm.IloscBrakujaca
                FROM ZlecenieMaterialy zm
                JOIN Material m ON zm.ID_Materialu = m.ID_Materialu
                WHERE zm.ID_Zlecenia = @ID";

            await using var matCmd = new SqlCommand(matSql, conn);
            matCmd.Parameters.AddWithValue("@ID", id);
            await using var matRdr = await matCmd.ExecuteReaderAsync();
            while (await matRdr.ReadAsync())
                detail.Materialy.Add(new ZlecenieMaterialVM
                {
                    ID_Materialu        = matRdr.GetInt32(0),
                    NazwaMaterialu     = matRdr.GetString(1),
                    Wymiary            = matRdr.GetString(2),
                    TypWysokosci       = matRdr.GetString(3),
                    Kolor              = matRdr.GetString(4),
                    IloscWymagana      = matRdr.GetInt32(5),
                    IloscZarezerwowana = matRdr.GetInt32(6),
                    IloscBrakujaca     = matRdr.GetInt32(7)
                });

            // Oblicz całkowity czas (TPZ + TJ * ilość)
            const string czasSql = @"
                SELECT ISNULL(SUM(ISNULL(Czas_Przygotowawczy_ms,0) +
                              CAST(ISNULL(Czas_Jednostkowy,0) AS bigint) * @Ilosc), 0)
                FROM Proces_Montazu WHERE ID_Wyrobu = @IDW";
            await using var czasCmd = new SqlCommand(czasSql, conn);
            czasCmd.Parameters.AddWithValue("@IDW",   detail.IDWyrobu ?? 0);
            czasCmd.Parameters.AddWithValue("@Ilosc", detail.IloscSztuk);
            detail.CalkowityCzasMs = Convert.ToInt32(await czasCmd.ExecuteScalarAsync() ?? 0);

            return detail;
        }

        // ── Tworzenie zlecenia ───────────────────────────────────────────
        /// <summary>
        /// Tworzy zlecenie, wykonuje backward scheduling i rezerwuje materiały.
        /// </summary>
        public async Task<(int idZlecenia, WalidacjaKomponentow? walidacja)> CreateZlecenieAsync(string nazwa, int iloscSztuk, int idWyrobu, string priorytet)
        {
            // 1. Walidacja dostępności komponentów
            var walidacja = await _inv.WalidujDostepnoscAsync(idWyrobu, iloscSztuk);
            if (!walidacja.CzyMozna)
                return (0, walidacja);

            // 2. Oblicz czas realizacji (TPZ + TJ * ilość) i backward scheduling
            int calkowityCzasMs = await ObliczCalkowityCzasAsync(idWyrobu, iloscSztuk);
            DateTime najpozniejszyStart = DateTime.Now.AddMilliseconds(-calkowityCzasMs);

            // 3. Sprawdź preempcję — czy jest aktywne zlecenie niższego priorytetu
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();

            int priorytetNum = Priorytety.ToNum(priorytet);
            int idNowego = 0;

            await using var tx = conn.BeginTransaction();
            try
            {
                // Wstaw zlecenie
                const string insertSql = @"
                    INSERT INTO Zlecenie_Produkcyjne
                        (Nazwa_Zlecenia, Ilosc_Sztuk, ID_Wyrobu, DueTime, Data_Realizacji,
                         Status_Zlecenia, Czas_Planowany_ms, Priorytet, PriorytetNum,
                         NajpozniejszyStart, CreatedAt)
                    VALUES (@Nazwa, @Ilosc, @Wyrob, @Due, CAST(@Due AS date),
                            'Nowe', @Czas, @Prior, @PriorNum, @NajpStart, GETDATE());
                    SELECT SCOPE_IDENTITY();";

                await using var cmd = new SqlCommand(insertSql, conn, tx);
                cmd.Parameters.AddWithValue("@Nazwa",    nazwa);
                cmd.Parameters.AddWithValue("@Ilosc",    iloscSztuk);
                cmd.Parameters.AddWithValue("@Wyrob",    idWyrobu);
                cmd.Parameters.AddWithValue("@Due", DateTime.Now);
                cmd.Parameters.AddWithValue("@Czas",     calkowityCzasMs);
                cmd.Parameters.AddWithValue("@Prior",    priorytet);
                cmd.Parameters.AddWithValue("@PriorNum", priorytetNum);
                cmd.Parameters.AddWithValue("@NajpStart", najpozniejszyStart);
                idNowego = Convert.ToInt32(await cmd.ExecuteScalarAsync());

                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }

            // 4. Rezerwuj materiały
            await _inv.RezerwujMaterialyAsync(idNowego, idWyrobu, iloscSztuk);

            // 5. Sprawdź preempcję — jeśli nowe ma wyższy priorytet od aktywnego, zatrzymaj aktywne
            await SprawdzPreempcjeAsync(idNowego, priorytetNum);

            _logger.LogInformation("[ORDER] Utworzono zlecenie {Id} ({Nazwa}), priorytet={P}",
                idNowego, nazwa, priorytet);

            return (idNowego, null);
        }

        // ── Edycja zlecenia ──────────────────────────────────────────────
        public async Task<WalidacjaKomponentow?> EditZlecenieAsync(int idZlecenia, string nazwa, int iloscSztuk, int idWyrobu, string priorytet)
        {
            var walidacja = await _inv.WalidujDostepnoscAsync(idWyrobu, iloscSztuk, idZlecenia);
            if (!walidacja.CzyMozna) return walidacja;

            int calkowityCzasMs     = await ObliczCalkowityCzasAsync(idWyrobu, iloscSztuk);
            DateTime najpoznStartu  = DateTime.Now.AddMilliseconds(-calkowityCzasMs);

            // Zwolnij stare rezerwy i zarezerwuj ponownie
            await _inv.ZwolnijRezerwyAsync(idZlecenia);

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(@"
                UPDATE Zlecenie_Produkcyjne
                SET Nazwa_Zlecenia    = @Nazwa,
                    Ilosc_Sztuk       = @Ilosc,
                    ID_Wyrobu         = @Wyrob,
                    DueTime           = @Due,
                    Data_Realizacji   = CAST(@Due AS date),
                    Czas_Planowany_ms = @Czas,
                    Priorytet         = @Prior,
                    PriorytetNum      = @PriorNum,
                    NajpozniejszyStart = @NajpStart
                WHERE ID_Zlecenia = @ID", conn);
            cmd.Parameters.AddWithValue("@Nazwa",    nazwa);
            cmd.Parameters.AddWithValue("@Ilosc",    iloscSztuk);
            cmd.Parameters.AddWithValue("@Wyrob",    idWyrobu);
            cmd.Parameters.AddWithValue("@Due", DateTime.Now);
            cmd.Parameters.AddWithValue("@Czas",     calkowityCzasMs);
            cmd.Parameters.AddWithValue("@Prior",    priorytet);
            cmd.Parameters.AddWithValue("@PriorNum", Priorytety.ToNum(priorytet));
            cmd.Parameters.AddWithValue("@NajpStart", najpoznStartu);
            cmd.Parameters.AddWithValue("@ID",       idZlecenia);
            await cmd.ExecuteNonQueryAsync();

            // Usuń stary wynik eksplozji BOM i wstaw nowy
            await using var delMat = new SqlCommand(
                "DELETE FROM ZlecenieMaterialy WHERE ID_Zlecenia = @ID", conn);
            delMat.Parameters.AddWithValue("@ID", idZlecenia);
            await delMat.ExecuteNonQueryAsync();

            await _inv.RezerwujMaterialyAsync(idZlecenia, idWyrobu, iloscSztuk);
            return null;
        }

        // ── Soft-delete ──────────────────────────────────────────────────
        public async Task SoftDeleteAsync(int idZlecenia)
        {
            await _inv.ZwolnijRezerwyAsync(idZlecenia);

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(@"
                UPDATE Zlecenie_Produkcyjne
                SET IsDeleted = 1, Status_Zlecenia = 'Anulowane'
                WHERE ID_Zlecenia = @ID", conn);
            cmd.Parameters.AddWithValue("@ID", idZlecenia);
            await cmd.ExecuteNonQueryAsync();
        }

        public async Task ClearAllAsync()
        {
            // Pobierz wszystkie aktywne zlecenia i zwolnij rezerwy
            const string sql = "SELECT ID_Zlecenia FROM Zlecenie_Produkcyjne WHERE IsDeleted = 0";
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            var ids = new List<int>();
            await using (var cmd = new SqlCommand(sql, conn))
            await using (var rdr = await cmd.ExecuteReaderAsync())
                while (await rdr.ReadAsync()) ids.Add(rdr.GetInt32(0));

            foreach (var id in ids)
                await _inv.ZwolnijRezerwyAsync(id);

            await using var upd = new SqlCommand(
                "UPDATE Zlecenie_Produkcyjne SET IsDeleted=1, Status_Zlecenia='Anulowane' WHERE IsDeleted=0",
                conn);
            await upd.ExecuteNonQueryAsync();
        }

        // ── Zmiana statusu ───────────────────────────────────────────────
        public async Task UpdateStatusAsync(int idZlecenia, string status)
        {
            var extra = status == "W toku"     ? ", StartedAt   = GETDATE()" :
                        status == "Zakonczone" ? ", CompletedAt = GETDATE()" : "";
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand($@"
                UPDATE Zlecenie_Produkcyjne
                SET Status_Zlecenia = @S {extra}
                WHERE ID_Zlecenia = @ID", conn);
            cmd.Parameters.AddWithValue("@S",  status);
            cmd.Parameters.AddWithValue("@ID", idZlecenia);
            await cmd.ExecuteNonQueryAsync();
        }

        // ── Preempcja priorytetów ────────────────────────────────────────
        /// <summary>
        /// Jeśli nowe zlecenie ma wyższy priorytet niż aktualnie "W toku",
        /// wstrzymuje bieżące i uruchamia nowe.
        /// </summary>
        public async Task SprawdzPreempcjeAsync(int idNowego, int priorytetNumNowego)
        {
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();

            // Znajdź aktualnie uruchomione zlecenie
            const string sql = @"
                SELECT TOP 1 ID_Zlecenia, PriorytetNum
                FROM Zlecenie_Produkcyjne
                WHERE Status_Zlecenia = 'W toku' AND IsDeleted = 0
                ORDER BY PriorytetNum DESC";

            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();

            if (!await rdr.ReadAsync()) return; // brak aktywnego — preempcja niepotrzebna
            int idAktywnego      = rdr.GetInt32(0);
            int priorytetAktywne = rdr.GetInt32(1);
            await rdr.CloseAsync();

            if (priorytetNumNowego > priorytetAktywne && idAktywnego != idNowego)
            {
                // Wstrzymaj bieżące
                await using var wst = new SqlCommand(@"
                    UPDATE Zlecenie_Produkcyjne SET Status_Zlecenia='Wstrzymane'
                    WHERE ID_Zlecenia = @ID", conn);
                wst.Parameters.AddWithValue("@ID", idAktywnego);
                await wst.ExecuteNonQueryAsync();

                // Uruchom nowe
                await using var start = new SqlCommand(@"
                    UPDATE Zlecenie_Produkcyjne SET Status_Zlecenia='W toku', StartedAt=GETDATE()
                    WHERE ID_Zlecenia = @ID", conn);
                start.Parameters.AddWithValue("@ID", idNowego);
                await start.ExecuteNonQueryAsync();

                _logger.LogInformation(
                    "[PREEMPCJA] Zlecenie {Stare} wstrzymane. Uruchamiam {Nowe} (wyższy priorytet)",
                    idAktywnego, idNowego);
            }
        }

        // ── Backward scheduling helper ───────────────────────────────────
        private async Task<int> ObliczCalkowityCzasAsync(int idWyrobu, int iloscSztuk)
        {
            // Uproszczenie: czas ciągły 24/7
            // TPZ (jednorazowy) + TJ * ilość dla każdej operacji w marszrucie
            const string sql = @"
                SELECT ISNULL(SUM(
                    ISNULL(Czas_Przygotowawczy_ms, 0) +
                    CAST(ISNULL(Czas_Jednostkowy, 0) AS bigint) * @Ilosc
                ), 0)
                FROM Proces_Montazu WHERE ID_Wyrobu = @IDW";

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@IDW",   idWyrobu);
            cmd.Parameters.AddWithValue("@Ilosc", iloscSztuk);
            var val = await cmd.ExecuteScalarAsync(); return val == DBNull.Value || val == null ? 0 : Convert.ToInt32(val);
        }

        // ── Mapper ───────────────────────────────────────────────────────
        private static ZlecenieVM MapZlecenieVM(SqlDataReader r) => new()
        {
            IDZlecenia        = r.GetInt32(0),
            NazwaZlecenia     = r.GetString(1),
            IloscSztuk        = r.GetInt32(2),
            SztukOK           = r.GetInt32(3),
            Wyprodukowano     = r.GetInt32(3),
            DataRealizacji    = r.IsDBNull(4) ? null : r.GetDateTime(4),
            DueTime           = r.IsDBNull(5) ? null : r.GetDateTime(5),
            StatusZlecenia    = r.IsDBNull(6) ? "Nowe" : r.GetString(6),
            CzasPlanowanyMs   = r.GetInt32(7),
            NazwaWyrobu       = r.IsDBNull(8) ? null : r.GetString(8),
            IDWyrobu          = r.IsDBNull(9) ? null : r.GetInt32(9),
            Priorytet         = r.IsDBNull(10) ? Priorytety.P3 : r.GetString(10),
            NajpozniejszyStart = r.IsDBNull(11) ? null : r.GetDateTime(11),
            CompletedAt       = r.IsDBNull(12) ? null : r.GetDateTime(12),
            SztukNOK          = r.IsDBNull(13) ? 0 : r.GetInt32(13)
        };

        public async Task NoweZajeciaResetAsync()
        {
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(_cs);
            await conn.OpenAsync();
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand("DELETE FROM Zlecenie_Produkcyjne; UPDATE Ustawienia_Maszyny SET Wymagany_Reset = 1;", conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}




