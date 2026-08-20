using LiniaProdukcyjnaDashboard.Models;
using Microsoft.Data.SqlClient;

namespace LiniaProdukcyjnaDashboard.Services
{
    /// <summary>
    /// Zarządza stanem magazynu komponentów LEGO:
    /// rezerwacja, zużycie, zwrot po NOK, reset do baseline, transfer ze schowka.
    /// </summary>
    public class InventoryService
    {
        private readonly string _cs;
        private readonly ILogger<InventoryService> _logger;

        public InventoryService(IConfiguration cfg, ILogger<InventoryService> logger)
        {
            _cs = cfg.GetConnectionString("BazaDanychRB")!;
            _logger = logger;
        }

        // ── Stan magazynu ────────────────────────────────────────────────
        public async Task<List<InventoryItem>> GetStanMagazynuAsync(string? lokalizacja = null)
        {
            var where = lokalizacja != null ? "WHERE Lokalizacja = @Lok" : "";
            var sql = $@"
                SELECT ID_Materialu, Nazwa_Materialu, ISNULL(Wymiary,''), ISNULL(TypWysokosci,''),
                       ISNULL(Kolor,''), StanBiezacy, IloscZarezerwowana, Lokalizacja
                FROM [dbo].[Material]
                {where}
                ORDER BY Lokalizacja, TypWysokosci, Wymiary, Kolor";

            var result = new List<InventoryItem>();
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            if (lokalizacja != null) cmd.Parameters.AddWithValue("@Lok", lokalizacja);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                result.Add(new InventoryItem
                {
                    IDMaterialu        = rdr.GetInt32(0),
                    NazwaMaterialu    = rdr.GetString(1),
                    Wymiary           = rdr.GetString(2),
                    TypWysokosci      = rdr.GetString(3),
                    Kolor             = rdr.GetString(4),
                    StanBiezacy       = rdr.GetInt32(5),
                    IloscZarezerwowana = rdr.GetInt32(6),
                    Lokalizacja       = rdr.GetString(7)
                });
            return result;
        }

        public async Task<List<InventoryTransaction>> GetTransakcjeAsync(int n = 50)
        {
            const string sql = @"
                SELECT TOP 50 t.ID, m.Nazwa_Materialu, t.TypTransakcji, t.Ilosc,
                       t.Timestamp, zp.Nazwa_Zlecenia, t.Notatka
                FROM InventoryTransactions t
                JOIN Material m ON t.ID_Materialu = m.ID_Materialu
                LEFT JOIN Zlecenie_Produkcyjne zp ON t.ID_Zlecenia = zp.ID_Zlecenia
                ORDER BY t.Timestamp DESC";

            var result = new List<InventoryTransaction>();
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                result.Add(new InventoryTransaction
                {
                    ID        = rdr.GetInt32(0),
                    Materiał  = rdr.GetString(1),
                    Typ       = rdr.GetString(2),
                    Ilosc     = rdr.GetInt32(3),
                    Timestamp = rdr.GetDateTime(4),
                    Zlecenie  = rdr.IsDBNull(5) ? null : rdr.GetString(5),
                    Notatka   = rdr.IsDBNull(6) ? null : rdr.GetString(6)
                });
            return result;
        }

        // ── Walidacja dostępności ────────────────────────────────────────
        /// <summary>
        /// Sprawdza czy magazyn MAIN ma wystarczające ilości dla zlecenia.
        /// Zwraca listę braków i maksymalną możliwą ilość do wyprodukowania.
        /// </summary>
        public async Task<WalidacjaKomponentow> WalidujDostepnoscAsync(
            int idWyrobu, int iloscSztuk, int? ignorujIdZlecenia = null)
        {
            // Pobierz BOM z podziałem per stanowisko
            const string bomSql = @"
                SELECT b.ID_Materialu, m.Nazwa_Materialu, ISNULL(m.Wymiary,''), ISNULL(m.Kolor,''),
                       b.Ilosc_Sztuk,
                       (m.StanBiezacy - m.IloscZarezerwowana) AS DostepneBezRezerwacji
                FROM Struktura_BOM b
                JOIN Material m ON b.ID_Materialu = m.ID_Materialu
                WHERE b.ID_Wyrobu = @IDW AND m.Lokalizacja = 'MAIN'";

            var wynik = new WalidacjaKomponentow { CzyMozna = true };
            int globalMaxSztuk = int.MaxValue;

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(bomSql, conn);
            cmd.Parameters.AddWithValue("@IDW", idWyrobu);
            await using var rdr = await cmd.ExecuteReaderAsync();

            while (await rdr.ReadAsync())
            {
                int    idMat         = rdr.GetInt32(0);
                string nazwa         = rdr.GetString(1);
                string wymiary       = rdr.GetString(2);
                string kolor         = rdr.GetString(3);
                int    iloscNaSztuke = (int)rdr.GetDecimal(4);
                int    dostepne      = rdr.GetInt32(5);

                int wymagane = iloscNaSztuke * iloscSztuk;
                int maxZTego = iloscNaSztuke > 0 ? dostepne / iloscNaSztuke : int.MaxValue;
                if (maxZTego < globalMaxSztuk) globalMaxSztuk = maxZTego;

                if (dostepne < wymagane)
                {
                    wynik.CzyMozna = false;
                    wynik.Braki.Add(new BrakKomponentu
                    {
                        IDMaterialu     = idMat,
                        NazwaMaterialu  = nazwa,
                        Wymiary         = wymiary,
                        Kolor           = kolor,
                        IloscWymagana   = wymagane,
                        IloscDostepna   = dostepne
                    });
                }
            }

            wynik.MaxMozliwaIlosc = globalMaxSztuk == int.MaxValue ? 0 : globalMaxSztuk;
            return wynik;
        }

        // ── Rezerwacja przy tworzeniu zlecenia ───────────────────────────
        public async Task RezerwujMaterialyAsync(int idZlecenia, int idWyrobu, int iloscSztuk)
        {
            const string bomSql = @"
                SELECT ID_Materialu, CAST(Ilosc_Sztuk AS int) FROM Struktura_BOM WHERE ID_Wyrobu = @IDW";

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var tx = conn.BeginTransaction();
            try
            {
                await using var cmd = new SqlCommand(bomSql, conn, tx);
                cmd.Parameters.AddWithValue("@IDW", idWyrobu);
                var bom = new List<(int idMat, int qty)>();
                await using (var rdr = await cmd.ExecuteReaderAsync())
                    while (await rdr.ReadAsync())
                        bom.Add((rdr.GetInt32(0), rdr.GetInt32(1) * iloscSztuk));

                foreach (var (idMat, qty) in bom)
                {
                    // Aktualizuj rezerwację w Material
                    await ExecuteNonQueryAsync(conn, tx,
                        "UPDATE Material SET IloscZarezerwowana = IloscZarezerwowana + @Q WHERE ID_Materialu = @ID",
                        ("@Q", qty), ("@ID", idMat));

                    // Zapisz wynik eksplozji BOM w ZlecenieMaterialy
                    await ExecuteNonQueryAsync(conn, tx, @"
                        INSERT INTO ZlecenieMaterialy (ID_Zlecenia, ID_Materialu, IloscWymagana, IloscZarezerwowana)
                        VALUES (@Zl, @Mat, @Wym, @Rez)",
                        ("@Zl", idZlecenia), ("@Mat", idMat), ("@Wym", qty), ("@Rez", qty));

                    // Log transakcji
                    await LogTransakcjiAsync(conn, tx, idMat, idZlecenia, "Rezerwacja", -qty,
                        $"Rezerwacja dla zlecenia {idZlecenia}");
                }

                await tx.CommitAsync();
                _logger.LogInformation("[INV] Zarezerwowano materiały dla zlecenia {Id}", idZlecenia);
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // ── Zwolnienie rezerw (przy soft-delete zlecenia) ────────────────
        public async Task ZwolnijRezerwyAsync(int idZlecenia)
        {
            const string sql = @"
                SELECT ID_Materialu, IloscZarezerwowana FROM ZlecenieMaterialy WHERE ID_Zlecenia = @Zl";

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var tx = conn.BeginTransaction();
            try
            {
                var rezerwy = new List<(int, int)>();
                await using (var cmd = new SqlCommand(sql, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@Zl", idZlecenia);
                    await using var rdr = await cmd.ExecuteReaderAsync();
                    while (await rdr.ReadAsync())
                        rezerwy.Add((rdr.GetInt32(0), rdr.GetInt32(1)));
                }

                foreach (var (idMat, qty) in rezerwy)
                {
                    await ExecuteNonQueryAsync(conn, tx,
                        "UPDATE Material SET IloscZarezerwowana = GREATEST(0, IloscZarezerwowana - @Q) WHERE ID_Materialu = @ID",
                        ("@Q", qty), ("@ID", idMat));
                    await LogTransakcjiAsync(conn, tx, idMat, idZlecenia, "ZwrotRezerwy", qty,
                        $"Zwolnienie rezerwy po usunięciu zlecenia {idZlecenia}");
                }

                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        }

        // ── Zużycie komponentów per stanowisko ───────────────────────────
        /// <summary>
        /// Wywołaj po ukończeniu sztuki na danym stanowisku (isNok=false).
        /// Pobiera BOM przypisany do tego stanowiska i odejmuje ze stanu.
        /// </summary>
        public async Task ZuzyjMaterialyNaStanowiskuAsync(int idZlecenia, int idWyrobu, int idStanowiska)
        {
            const string bomSql = @"
                SELECT b.ID_Materialu, CAST(b.Ilosc_Sztuk AS int)
                FROM Struktura_BOM b
                JOIN Material m ON b.ID_Materialu = m.ID_Materialu
                WHERE b.ID_Wyrobu = @IDW
                  AND (b.ID_Stanowiska = @IDSt OR (b.ID_Stanowiska IS NULL AND @IDSt = 4))
                  AND m.Lokalizacja = 'MAIN'";

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var tx = conn.BeginTransaction();
            try
            {
                var bom = new List<(int, int)>();
                await using (var cmd = new SqlCommand(bomSql, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@IDW",  idWyrobu);
                    cmd.Parameters.AddWithValue("@IDSt", idStanowiska);
                    await using var rdr = await cmd.ExecuteReaderAsync();
                    while (await rdr.ReadAsync())
                        bom.Add((rdr.GetInt32(0), rdr.GetInt32(1)));
                }

                foreach (var (idMat, qty) in bom)
                {
                    await ExecuteNonQueryAsync(conn, tx, @"
                        UPDATE Material
                        SET StanBiezacy       = GREATEST(0, StanBiezacy - @Q),
                            IloscZarezerwowana = GREATEST(0, IloscZarezerwowana - @Q),
                            AktualizacjaAt    = GETDATE()
                        WHERE ID_Materialu = @ID",
                        ("@Q", qty), ("@ID", idMat));
                    await LogTransakcjiAsync(conn, tx, idMat, idZlecenia, "Zuzycie", -qty,
                        $"Zużycie na stanowisku {idStanowiska}");
                }

                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        }

        // ── Zwrot po NOK ─────────────────────────────────────────────────
        /// <summary>
        /// Zwraca do magazynu wszystkie komponenty zużyte dla danej sztuki na wszystkich stacjach.
        /// Wywołaj gdy QC zwróci NOK (stacja 4).
        /// </summary>
        public async Task ZwrocMaterialyPoNokAsync(int idZlecenia, int idWyrobu)
        {
            // Zwracamy WSZYSTKIE komponenty z BOM (wszystkich stacji) — bo sztuka jest złomowana
            const string bomSql = @"
                SELECT b.ID_Materialu, CAST(b.Ilosc_Sztuk AS int)
                FROM Struktura_BOM b
                JOIN Material m ON b.ID_Materialu = m.ID_Materialu
                WHERE b.ID_Wyrobu = @IDW AND m.Lokalizacja = 'MAIN'";

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var tx = conn.BeginTransaction();
            try
            {
                var bom = new List<(int, int)>();
                await using (var cmd = new SqlCommand(bomSql, conn, tx))
                {
                    cmd.Parameters.AddWithValue("@IDW", idWyrobu);
                    await using var rdr = await cmd.ExecuteReaderAsync();
                    while (await rdr.ReadAsync())
                        bom.Add((rdr.GetInt32(0), rdr.GetInt32(1)));
                }

                foreach (var (idMat, qty) in bom)
                {
                    await ExecuteNonQueryAsync(conn, tx, @"
                        UPDATE Material SET StanBiezacy = StanBiezacy + @Q, AktualizacjaAt = GETDATE()
                        WHERE ID_Materialu = @ID",
                        ("@Q", qty), ("@ID", idMat));
                    await LogTransakcjiAsync(conn, tx, idMat, idZlecenia, "ZwrotPoNOK", qty,
                        $"Zwrot po NOK dla zlecenia {idZlecenia}");
                }

                await tx.CommitAsync();
                _logger.LogInformation("[INV] Zwrócono materiały po NOK dla zlecenia {Id}", idZlecenia);
            }
            catch { await tx.RollbackAsync(); throw; }
        }

        // ── Reset magazynu do stanu bazowego ────────────────────────────
        public async Task ResetujMagazynAsync(int? idOperatora = null)
        {
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var tx = conn.BeginTransaction();
            try
            {
                // Przywróć stany z InventoryBaseline
                await ExecuteNonQueryAsync(conn, tx, @"
                    UPDATE m
                    SET m.StanBiezacy        = b.IloscBazowa,
                        m.IloscZarezerwowana = 0,
                        m.AktualizacjaAt     = GETDATE()
                    FROM Material m
                    JOIN InventoryBaseline b ON m.ID_Materialu = b.ID_Materialu");

                // Log
                await ExecuteNonQueryAsync(conn, tx, @"
                    INSERT INTO InventoryTransactions (ID_Materialu, ID_Operatora, TypTransakcji, Ilosc, Notatka)
                    SELECT b.ID_Materialu, @Op, 'ResetMagazynu', b.IloscBazowa,
                           'Reset do stanu bazowego 15.07.2025'
                    FROM InventoryBaseline b",
                    ("@Op", (object?)idOperatora ?? DBNull.Value));

                await tx.CommitAsync();
                _logger.LogInformation("[INV] Reset magazynu do stanu bazowego wykonany przez op={Op}", idOperatora);
            }
            catch { await tx.RollbackAsync(); throw; }
        }

        // ── Transfer ze schowka do MAIN ──────────────────────────────────
        public async Task PrzesunZeSchowkaAsync(int idMaterialu, int ilosc, int? idOperatora = null)
        {
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var tx = conn.BeginTransaction();
            try
            {
                // Sprawdź dostępność w schowku
                await using var check = new SqlCommand(
                    "SELECT StanBiezacy FROM Material WHERE ID_Materialu=@ID AND Lokalizacja='ZAPAS_SCHOWEK'",
                    conn, tx);
                check.Parameters.AddWithValue("@ID", idMaterialu);
                var stanSchowek = (int?)await check.ExecuteScalarAsync();
                if (stanSchowek == null || stanSchowek < ilosc)
                    throw new InvalidOperationException("Niewystarczający stan w schowku.");

                // Zmniejsz schowek
                await ExecuteNonQueryAsync(conn, tx,
                    "UPDATE Material SET StanBiezacy = StanBiezacy - @Q WHERE ID_Materialu=@ID AND Lokalizacja='ZAPAS_SCHOWEK'",
                    ("@Q", ilosc), ("@ID", idMaterialu));

                // Znajdź odpowiednik w MAIN (ta sama nazwa/wymiary/kolor)
                await using var findMain = new SqlCommand(@"
                    SELECT TOP 1 m_main.ID_Materialu
                    FROM Material m_schowek
                    JOIN Material m_main ON m_main.Wymiary = m_schowek.Wymiary
                        AND m_main.TypWysokosci = m_schowek.TypWysokosci
                        AND m_main.Kolor = m_schowek.Kolor
                        AND m_main.Lokalizacja = 'MAIN'
                    WHERE m_schowek.ID_Materialu = @ID", conn, tx);
                findMain.Parameters.AddWithValue("@ID", idMaterialu);
                var idMain = (int?)await findMain.ExecuteScalarAsync();

                if (idMain.HasValue)
                {
                    await ExecuteNonQueryAsync(conn, tx,
                        "UPDATE Material SET StanBiezacy = StanBiezacy + @Q WHERE ID_Materialu=@ID",
                        ("@Q", ilosc), ("@ID", idMain.Value));
                    await LogTransakcjiAsync(conn, tx, idMain.Value, null, "Transfer", ilosc,
                        $"Transfer ze schowka (ID={idMaterialu})");
                }
                else
                {
                    // Brak odpowiednika w MAIN — zmień lokalizację rekordu
                    await ExecuteNonQueryAsync(conn, tx,
                        "UPDATE Material SET Lokalizacja='MAIN', StanBiezacy=StanBiezacy+@Q WHERE ID_Materialu=@ID",
                        ("@Q", ilosc), ("@ID", idMaterialu));
                    await LogTransakcjiAsync(conn, tx, idMaterialu, null, "Transfer", ilosc,
                        "Transfer ze schowka do MAIN (nowy rekord)");
                }

                await tx.CommitAsync();
            }
            catch { await tx.RollbackAsync(); throw; }
        }

        // ── Helpers ──────────────────────────────────────────────────────
        private static async Task ExecuteNonQueryAsync(
            SqlConnection conn, SqlTransaction tx, string sql,
            params (string name, object? value)[] parameters)
        {
            await using var cmd = new SqlCommand(sql, conn, tx);
            foreach (var (name, value) in parameters)
                cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            await cmd.ExecuteNonQueryAsync();
        }

        private static async Task LogTransakcjiAsync(
            SqlConnection conn, SqlTransaction tx,
            int idMat, int? idZlecenia, string typ, int ilosc, string? notatka)
        {
            await ExecuteNonQueryAsync(conn, tx, @"
                INSERT INTO InventoryTransactions (ID_Materialu, ID_Zlecenia, TypTransakcji, Ilosc, Notatka)
                VALUES (@M, @Z, @T, @I, @N)",
                ("@M", idMat),
                ("@Z", (object?)idZlecenia ?? DBNull.Value),
                ("@T", typ),
                ("@I", ilosc),
                ("@N", (object?)notatka ?? DBNull.Value));
        }
    }
}
