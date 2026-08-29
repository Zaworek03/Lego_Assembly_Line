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

            ZlecenieVM vm;
            await using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@ID", id);
                await using var rdr = await cmd.ExecuteReaderAsync();
                if (!await rdr.ReadAsync()) return null;
                vm = MapZlecenieVM(rdr);
            }

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

            // Czytnik musi byc zamkniety zanim na tym samym polaczeniu poleci kolejne
            // zapytanie - inaczej SqlClient rzuca "otwarty DataReader" i modal sie nie otwiera.
            await using (var matCmd = new SqlCommand(matSql, conn))
            {
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
            }

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
        /// <param name="ignorujBraki">
        /// Gdy true, zlecenie powstaje mimo niewystarczajacego stanu magazynu.
        /// Rezerwacja i tak zostanie zapisana, wiec magazyn wejdzie na minus
        /// (ilosc dostepna ujemna) - swiadome "zadluzenie" zatwierdzone przez uzytkownika.
        /// </param>
        public async Task<(int idZlecenia, WalidacjaKomponentow? walidacja)> CreateZlecenieAsync(
            string nazwa, int iloscSztuk, int idWyrobu, string priorytet, bool ignorujBraki = false)
        {
            // 1. Walidacja dostępności komponentów
            var walidacja = await _inv.WalidujDostepnoscAsync(idWyrobu, iloscSztuk);
            if (!walidacja.CzyMozna && !ignorujBraki)
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

                // Nazwa sprzezona z ID_Zlecenia (ten sam numer idzie do PLC jako NastepneZlecenie.Zlecenie.ID),
                // zamiast liczenia widocznych wierszy na stronie (co resetowalo sie po usunieciu zlecen).
                await using var nameCmd = new SqlCommand(
                    "UPDATE Zlecenie_Produkcyjne SET Nazwa_Zlecenia = @Nazwa WHERE ID_Zlecenia = @ID", conn, tx);
                nameCmd.Parameters.AddWithValue("@Nazwa", $"ZL{idNowego:D3}");
                nameCmd.Parameters.AddWithValue("@ID", idNowego);
                await nameCmd.ExecuteNonQueryAsync();

                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }

            // 4. Rezerwuj materiały
            await _inv.RezerwujMaterialyAsync(idNowego, idWyrobu, iloscSztuk);

            // 5. Sprawdź preempcję — jeśli nowe ma wyższy priorytet od aktywnego, zatrzymaj aktywne
            await SprawdzPreempcjeAsync(idNowego, priorytetNum);

            if (ignorujBraki && !walidacja.CzyMozna)
                _logger.LogWarning("[ORDER] Zlecenie {Id} utworzone MIMO BRAKOW - magazyn na minusie ({N} pozycji)",
                    idNowego, walidacja.Braki.Count);

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
            // PriorytetNum to w bazie tinyint (czyli Byte) - GetInt32 rzucalby wyjatkiem rzutowania.
            int priorytetAktywne = Convert.ToInt32(rdr[1]);
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

        /// <summary>
        /// Pelny reset: kasuje zlecenia ORAZ powiazana z nimi historie (Realizacja_Produkcji,
        /// Koszty, Wskazniki, Harmonogram, ZlecenieMaterialy), zeruje rezerwacje materialow
        /// i resetuje licznik ID_Zlecenia do 0 - kolejne zlecenie dostanie ZL001.
        /// Nieodwracalne (historia OEE/kosztow dla usuwanych zlecen ginie na stale).
        /// </summary>
        public async Task NoweZajeciaResetAsync()
        {
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(_cs);
            await conn.OpenAsync();
            using var tx = conn.BeginTransaction();
            try
            {
                // NAJPIERW raport - dane sa kasowane ponizej, wiec zapis musi je wyprzedzic.
                await ZapiszRaportAsync(conn, tx);

                // Blok "Wydajnosc cyklu" ma przezyc reset - czasy cykli przepisujemy
                // do trwalego archiwum, zanim Realizacja_Produkcji zostanie skasowana.
                await ArchiwizujCykleAsync(conn, tx);

                // Kolejnosc wymuszona przez FK: najpierw "liscie" (Koszty/Wskazniki zaleza
                // od Realizacja_Produkcji), potem Realizacja_Produkcji, na koncu Zlecenie_Produkcyjne.
                using var cmd = new Microsoft.Data.SqlClient.SqlCommand(@"
                    -- SztukiPrzetworzone MUSI lecieć razem z resztą. To tabela odporności
                    -- na duplikaty QC (klucz: ID_Zlecenia + PartNo). Po resecie IDENTITY
                    -- zlecen wraca do 0, wiec nowe ZL001 znow dostaje ID_Zlecenia=1, a jego
                    -- pierwsza sztuka PartNo=1 - czyli parę, ktora tu juz leżała z poprzednich
                    -- zajec. ZarejestrujSztukePoQC uznawal ja wtedy za juz policzona, wychodzil
                    -- przed IncrementQcWynik i zlecenie NIGDY nie dobijalo do Ilosc_Sztuk,
                    -- wiec nie zmienialo statusu na 'Zakonczone'.
                    DELETE FROM SztukiPrzetworzone;
                    DELETE FROM Koszty;
                    DELETE FROM Wskazniki;
                    DELETE FROM Harmonogram;
                    DELETE FROM Realizacja_Produkcji;
                    DELETE FROM ZlecenieMaterialy;
                    DELETE FROM Zlecenie_Produkcyjne;
                    DBCC CHECKIDENT ('Zlecenie_Produkcyjne', RESEED, 0);
                    UPDATE Material SET IloscZarezerwowana = 0;
                    -- Baseline = aktualny licznik z PLC, wiec 'wyprodukowano dzisiaj' startuje od zera.
                    UPDATE Ustawienia_Maszyny SET Wymagany_Reset = 1,
                                                  Baseline_Dzisiaj = Wyprodukowano_Ogolem;", conn, tx);
                await cmd.ExecuteNonQueryAsync();
                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        }

        /// <summary>
        /// Przepisuje historie produkcji z Realizacja_Produkcji do trwalej HistoriaCykli.
        /// Realizacja_Produkcji wisi na FK do Zlecenie_Produkcyjne i musi zniknac przy
        /// resecie zajec, ale bloki "Wydajnosc cyklu" i "Popularnosc wyrobow" maja
        /// pokazywac dane bez przerwy - licza z okna ostatnich cykli/sztuk, a nie od zera.
        /// Archiwizujemy WSZYSTKIE wiersze (tez te z zerowym czasem, np. z QC) - popularnosc
        /// liczy sztuki ze stanowiska 4, a filtr na czas > 0 nakladamy dopiero w zapytaniu.
        /// </summary>
        private static async Task ArchiwizujCykleAsync(SqlConnection conn, SqlTransaction tx)
        {
            await using var cmd = new SqlCommand(@"
                INSERT INTO HistoriaCykli (ID_Wyrobu, ID_Stanowiska, Czas_Cyklu_ms, Czas_Zadany_ms, Czas_Zakonczenia)
                SELECT zp.ID_Wyrobu, r.ID_Stanowiska, r.Czas_Cyklu_ms,
                       ISNULL(pm.Czas_Jednostkowy, 0), r.Czas_Zakonczenia
                FROM Realizacja_Produkcji r
                JOIN Zlecenie_Produkcyjne zp ON zp.ID_Zlecenia = r.ID_Zlecenia
                LEFT JOIN Proces_Montazu  pm ON pm.ID_Wyrobu   = zp.ID_Wyrobu
                                             AND pm.ID_Stanowiska = r.ID_Stanowiska;", conn, tx);
            await cmd.ExecuteNonQueryAsync();
        }

        /// <summary>
        /// Zapisuje migawke bloku zajec do raportow. Wywolywane w tej samej transakcji
        /// co reset, ZANIM dane zostana skasowane.
        /// </summary>
        private static async Task ZapiszRaportAsync(SqlConnection conn, SqlTransaction tx)
        {
            // Nic nie produkowano i nie bylo zlecen - nie ma sensu tworzyc pustego raportu.
            await using (var check = new SqlCommand(
                "SELECT COUNT(*) FROM Zlecenie_Produkcyjne", conn, tx))
            {
                if (Convert.ToInt32(await check.ExecuteScalarAsync()) == 0) return;
            }

            const string naglowekSql = @"
                INSERT INTO Raporty (Nazwa, OEE, Dostepnosc, Wydajnosc, Jakosc, FPY, SztukOK, SztukNOK)
                SELECT
                    'Zajęcia ' + CONVERT(varchar, GETDATE(), 120),
                    ISNULL((SELECT AVG(CAST(Wskaznik_OEE AS float)) FROM Wskazniki), 0),
                    ISNULL((SELECT AVG(CAST(Dostepnosc   AS float)) FROM Wskazniki), 0),
                    ISNULL((SELECT AVG(CAST(Wydajnosc    AS float)) FROM Wskazniki), 0),
                    ISNULL((SELECT AVG(CAST(Jakosc       AS float)) FROM Wskazniki), 0),
                    CASE WHEN SUM(ISNULL(SztukOK,0)) + SUM(ISNULL(SztukNOK,0)) > 0
                         THEN CAST(SUM(ISNULL(SztukOK,0)) AS float)
                              / (SUM(ISNULL(SztukOK,0)) + SUM(ISNULL(SztukNOK,0)))
                         ELSE 0 END,
                    SUM(ISNULL(SztukOK,0)),
                    SUM(ISNULL(SztukNOK,0))
                FROM Zlecenie_Produkcyjne;
                SELECT SCOPE_IDENTITY();";

            int idRaportu;
            await using (var cmd = new SqlCommand(naglowekSql, conn, tx))
                idRaportu = Convert.ToInt32(await cmd.ExecuteScalarAsync());

            // Historia zlecen - wszystkie, razem ze statusami
            await using (var cmd = new SqlCommand(@"
                INSERT INTO RaportZlecenia (ID_Raportu, Nazwa, Wyrob, Status, IloscSztuk, SztukOK, SztukNOK)
                SELECT @R, zp.Nazwa_Zlecenia, w.Nazwa_Wyrobu, zp.Status_Zlecenia,
                       zp.Ilosc_Sztuk, ISNULL(zp.SztukOK,0), ISNULL(zp.SztukNOK,0)
                FROM Zlecenie_Produkcyjne zp
                LEFT JOIN Wyrob w ON zp.ID_Wyrobu = w.ID_Wyrobu
                ORDER BY zp.ID_Zlecenia", conn, tx))
            {
                cmd.Parameters.AddWithValue("@R", idRaportu);
                await cmd.ExecuteNonQueryAsync();
            }

            // Materialy FAKTYCZNIE zuzyte - tylko te przypisane do zlecen w tym bloku,
            // proporcjonalnie do liczby sztuk, ktore przeszly przez linie (OK + NOK).
            // Nie sa to ogolne stany magazynowe.
            await using (var cmd = new SqlCommand(@"
                INSERT INTO RaportMaterialy (ID_Raportu, Nazwa, Zuzyto)
                SELECT @R, m.Nazwa_Materialu,
                       SUM(CAST(zm.IloscWymagana AS float) / NULLIF(zp.Ilosc_Sztuk,0)
                           * (ISNULL(zp.SztukOK,0) + ISNULL(zp.SztukNOK,0)))
                FROM ZlecenieMaterialy zm
                JOIN Zlecenie_Produkcyjne zp ON zm.ID_Zlecenia = zp.ID_Zlecenia
                JOIN Material m              ON zm.ID_Materialu = m.ID_Materialu
                GROUP BY m.Nazwa_Materialu
                HAVING SUM(CAST(zm.IloscWymagana AS float) / NULLIF(zp.Ilosc_Sztuk,0)
                           * (ISNULL(zp.SztukOK,0) + ISNULL(zp.SztukNOK,0))) > 0", conn, tx))
            {
                cmd.Parameters.AddWithValue("@R", idRaportu);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // ── Odczyt raportow ──────────────────────────────────────────────
        /// <summary>
        /// Kasuje raport razem z jego pozycjami. Kolejnosc wymuszona przez FK:
        /// najpierw dzieci (RaportMaterialy, RaportZlecenia), potem naglowek.
        /// Wszystko w jednej transakcji, zeby nie zostal raport bez pozycji.
        /// </summary>
        public async Task UsunRaportAsync(int idRaportu)
        {
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var tx = (SqlTransaction)await conn.BeginTransactionAsync();
            try
            {
                await using var cmd = new SqlCommand(@"
                    DELETE FROM RaportMaterialy WHERE ID_Raportu = @R;
                    DELETE FROM RaportZlecenia  WHERE ID_Raportu = @R;
                    DELETE FROM Raporty         WHERE ID = @R;", conn, tx);
                cmd.Parameters.AddWithValue("@R", idRaportu);
                await cmd.ExecuteNonQueryAsync();
                await tx.CommitAsync();
                _logger.LogInformation("Usunieto raport {Id}", idRaportu);
            }
            catch { await tx.RollbackAsync(); throw; }
        }

        public async Task<List<Raport>> GetRaportyAsync(bool zeSzczegolami = true)
        {
            var raporty = new List<Raport>();
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();

            await using (var cmd = new SqlCommand(@"
                SELECT r.ID, r.Nazwa, r.Utworzono, r.OEE, r.Dostepnosc, r.Wydajnosc, r.Jakosc,
                       r.FPY, r.SztukOK, r.SztukNOK,
                       (SELECT COUNT(*) FROM RaportZlecenia rz WHERE rz.ID_Raportu = r.ID)
                FROM Raporty r ORDER BY r.Utworzono DESC", conn))
            await using (var rdr = await cmd.ExecuteReaderAsync())
            {
                while (await rdr.ReadAsync())
                    raporty.Add(new Raport
                    {
                        ID = rdr.GetInt32(0), Nazwa = rdr.GetString(1), Utworzono = rdr.GetDateTime(2),
                        OEE = rdr.GetDouble(3), Dostepnosc = rdr.GetDouble(4),
                        Wydajnosc = rdr.GetDouble(5), Jakosc = rdr.GetDouble(6), FPY = rdr.GetDouble(7),
                        SztukOK = rdr.GetInt32(8), SztukNOK = rdr.GetInt32(9),
                        LiczbaZlecen = rdr.GetInt32(10)
                    });
            }

            if (!zeSzczegolami || raporty.Count == 0) return raporty;

            await using (var cmd = new SqlCommand(
                "SELECT ID_Raportu, Nazwa, Wyrob, Status, IloscSztuk, SztukOK, SztukNOK FROM RaportZlecenia ORDER BY ID", conn))
            await using (var rdr = await cmd.ExecuteReaderAsync())
            {
                while (await rdr.ReadAsync())
                    raporty.FirstOrDefault(r => r.ID == rdr.GetInt32(0))?.Zlecenia.Add(new RaportZlecenie
                    {
                        Nazwa = rdr.GetString(1),
                        Wyrob = rdr.IsDBNull(2) ? null : rdr.GetString(2),
                        Status = rdr.GetString(3), IloscSztuk = rdr.GetInt32(4),
                        SztukOK = rdr.GetInt32(5), SztukNOK = rdr.GetInt32(6)
                    });
            }

            await using (var cmd = new SqlCommand(
                "SELECT ID_Raportu, Nazwa, Zuzyto FROM RaportMaterialy ORDER BY Zuzyto DESC", conn))
            await using (var rdr = await cmd.ExecuteReaderAsync())
            {
                while (await rdr.ReadAsync())
                    raporty.FirstOrDefault(r => r.ID == rdr.GetInt32(0))?.Materialy.Add(new RaportMaterial
                    {
                        Nazwa = rdr.GetString(1), Zuzyto = rdr.GetInt32(2)
                    });
            }

            return raporty;
        }
    }
}




