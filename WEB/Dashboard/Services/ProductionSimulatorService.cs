using LiniaProdukcyjnaDashboard.Models;
using Microsoft.Data.SqlClient;

namespace LiniaProdukcyjnaDashboard.Services
{
    public class ProductionSimulatorService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly string _cs;
        private readonly ILogger<ProductionSimulatorService> _logger;
        private readonly Random _rand = new();

        // Kod postoju gdy brak aktywnych zleceń
        private const string KOD_BRAK_ZLECEN = "BRAK-ZLECEN";
        private bool _bylPostojBrakZlecen = false;

        public ProductionSimulatorService(
            IServiceScopeFactory scopeFactory, IConfiguration cfg,
            ILogger<ProductionSimulatorService> logger)
        {
            _scopeFactory = scopeFactory;
            _cs           = cfg.GetConnectionString("BazaDanychRB")!;
            _logger       = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Symulator potoku produkcji uruchomiony.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var conn = new SqlConnection(_cs);
                    await conn.OpenAsync(stoppingToken);

                    // ── Pobierz aktywne zlecenia „W toku" (posortowane wg priorytetu) ──
                    var cmd = new SqlCommand(@"
                        SELECT ID_Zlecenia, ID_Wyrobu
                        FROM dbo.Zlecenie_Produkcyjne
                        WHERE Status_Zlecenia = 'W toku' AND IsDeleted = 0
                        ORDER BY PriorytetNum DESC", conn);

                    var zlecenia = new List<(int idZlecenia, int? idWyrobu)>();
                    await using (var rdr = await cmd.ExecuteReaderAsync(stoppingToken))
                        while (await rdr.ReadAsync(stoppingToken))
                            zlecenia.Add((rdr.GetInt32(0), rdr.IsDBNull(1) ? null : rdr.GetInt32(1)));

                    if (zlecenia.Count == 0)
                    {
                        // ── Brak zleceń — loguj przestój ──────────────────────────
                        if (!_bylPostojBrakZlecen)
                        {
                            await ZapiszPostojBrakZlecenAsync(conn, stoppingToken);
                            _bylPostojBrakZlecen = true;
                        }
                    }
                    else
                    {
                        _bylPostojBrakZlecen = false;

                        // Sprawdź czy jest wstrzymane zlecenie wyższego priorytetu do wznowienia
                        await WznowNajwyzszePriorytetAsync(conn, zlecenia[0].idZlecenia, stoppingToken);

                        // Uruchom symulację jednej sztuki dla każdego aktywnego zlecenia
                        foreach (var (idZlecenia, idWyrobu) in zlecenia)
                            _ = SimulateSinglePieceJourneyAsync(idZlecenia, idWyrobu ?? 0, stoppingToken);
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Błąd symulatora głównego");
                }

                // Takt linii: nowa sztuka co 30-40 sekund
                await Task.Delay(TimeSpan.FromSeconds(_rand.Next(30, 40)), stoppingToken);
            }
        }

        private async Task SimulateSinglePieceJourneyAsync(
            int idZlecenia, int idWyrobu, CancellationToken ct)
        {
            int[] stations   = { 1, 2, 3, 4 };
            int   operatorId = 2;

            foreach (var st in stations)
            {
                if (ct.IsCancellationRequested) break;

                // Sprawdź czy zlecenie nadal „W toku" (mogło zostać wstrzymane przez preempcję)
                if (!await CzyZlecenieWTokuAsync(idZlecenia, ct)) break;

                int  splywMs  = _rand.Next(25000, 35000);
                await Task.Delay(splywMs, ct);

                int  cyklMs   = splywMs - _rand.Next(500, 3000);
                int  postojMs = splywMs - cyklMs;
                bool isNok    = _rand.NextDouble() < 0.02;
                bool qc       = !isNok;

                var end   = DateTime.Now;
                var start = end.AddMilliseconds(-splywMs);

                try
                {
                    await using var conn = new SqlConnection(_cs);
                    await conn.OpenAsync(ct);

                    // Zapisz realizację na tym stanowisku
                    var insertSql = @"
                        INSERT INTO dbo.Realizacja_Produkcji
                        (ID_Zlecenia, ID_Stanowiska, ID_Operatora,
                         Czas_Rozpoczecia, Czas_Zakonczenia,
                         Czas_Splywu_ms, Czas_Cyklu_ms, Czas_Postoju_ms,
                         Ilosc_Wyprodukowanych, Liczba_Wadliwych, Wynik_QC)
                        VALUES (@Zl, @St, @Op, @Start, @End,
                                @Splyw, @Cykl, @Postoj, @Ilosc, @Wada, @QC)";

                    using var insertCmd = new SqlCommand(insertSql, conn);
                    insertCmd.Parameters.AddWithValue("@Zl",    idZlecenia);
                    insertCmd.Parameters.AddWithValue("@St",    st);
                    insertCmd.Parameters.AddWithValue("@Op",    operatorId);
                    insertCmd.Parameters.AddWithValue("@Start", start);
                    insertCmd.Parameters.AddWithValue("@End",   end);
                    insertCmd.Parameters.AddWithValue("@Splyw", splywMs);
                    insertCmd.Parameters.AddWithValue("@Cykl",  cyklMs);
                    insertCmd.Parameters.AddWithValue("@Postoj",postojMs);
                    insertCmd.Parameters.AddWithValue("@Ilosc", 1);
                    insertCmd.Parameters.AddWithValue("@Wada",  isNok ? 1 : 0);
                    insertCmd.Parameters.AddWithValue("@QC",    qc);
                    await insertCmd.ExecuteNonQueryAsync(ct);

                    _logger.LogInformation(
                        "[POTOK] Zlecenie {Zl} | Stacja {St} | OK={QC}", idZlecenia, st, qc);

                    // ── Zużycie komponentów per stanowisko ────────────────────
                    if (!isNok && idWyrobu > 0)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var inv = scope.ServiceProvider.GetRequiredService<InventoryService>();
                        await inv.ZuzyjMaterialyNaStanowiskuAsync(idZlecenia, idWyrobu, st);
                    }

                    // ── QC: wynik końcowy (stacja 4) ──────────────────────────
                    if (st == 4)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var inv = scope.ServiceProvider.GetRequiredService<InventoryService>();

                        if (isNok)
                        {
                            // Zwróć wszystkie komponenty zużyte na tej sztuce
                            await inv.ZwrocMaterialyPoNokAsync(idZlecenia, idWyrobu);
                            // Zaktualizuj licznik NOK
                            await AktualizujSztukiAsync(conn, idZlecenia, ok: false, ct);
                        }
                        else
                        {
                            // Zaktualizuj licznik OK
                            await AktualizujSztukiAsync(conn, idZlecenia, ok: true, ct);
                            // Sprawdź auto-zakończenie
                            await TryAutoZakonczZlecenieAsync(idZlecenia, conn, ct);
                        }
                    }
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Błąd zapisu symulatora (zlecenie={Zl}, stacja={St})",
                        idZlecenia, st);
                }

                // Sztuka wadliwa — wyrzucamy z potoku
                if (isNok)
                {
                    _logger.LogWarning(
                        "[ODRZUT] Zlecenie {Zl} — sztuka odrzucona na stacji {St}", idZlecenia, st);
                    break;
                }
            }
        }

        // ── Auto-zakończenie po osiągnięciu planu ────────────────────────
        private async Task TryAutoZakonczZlecenieAsync(
            int idZlecenia, SqlConnection conn, CancellationToken ct)
        {
            const string checkSql = @"
                SELECT Ilosc_Sztuk, SztukOK
                FROM dbo.Zlecenie_Produkcyjne
                WHERE ID_Zlecenia = @Zl AND Status_Zlecenia = 'W toku' AND IsDeleted = 0";

            await using var checkCmd = new SqlCommand(checkSql, conn);
            checkCmd.Parameters.AddWithValue("@Zl", idZlecenia);
            await using var rdr = await checkCmd.ExecuteReaderAsync(ct);
            if (!await rdr.ReadAsync(ct)) return;
            int plan  = rdr.GetInt32(0);
            int dobre = rdr.GetInt32(1);
            await rdr.CloseAsync();

            if (dobre < plan) return;

            await using var upd = new SqlCommand(@"
                UPDATE dbo.Zlecenie_Produkcyjne
                SET Status_Zlecenia = 'Zakonczone', CompletedAt = GETDATE()
                WHERE ID_Zlecenia = @Zl AND Status_Zlecenia = 'W toku'", conn);
            upd.Parameters.AddWithValue("@Zl", idZlecenia);
            int rows = await upd.ExecuteNonQueryAsync(ct);

            if (rows > 0)
            {
                _logger.LogInformation(
                    "[AUTO-ZAKOŃCZENIE] Zlecenie {Zl} ukończone ({Ok}/{Plan} szt.)",
                    idZlecenia, dobre, plan);

                // Po zakończeniu — sprawdź czy jest wstrzymane zlecenie do wznowienia
                await WznowNastepnePoZakonczeniumAsync(conn, ct);
            }
        }

        // ── Aktualizacja liczników OK/NOK ────────────────────────────────
        private static async Task AktualizujSztukiAsync(
            SqlConnection conn, int idZlecenia, bool ok, CancellationToken ct)
        {
            var col = ok ? "SztukOK" : "SztukNOK";
            await using var cmd = new SqlCommand(
                $"UPDATE Zlecenie_Produkcyjne SET {col} = {col} + 1 WHERE ID_Zlecenia = @ID",
                conn);
            cmd.Parameters.AddWithValue("@ID", idZlecenia);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        // ── Preempcja: sprawdź czy jest wstrzymane z wyższym priorytetem ─
        private async Task WznowNajwyzszePriorytetAsync(
            SqlConnection conn, int idAktywnego, CancellationToken ct)
        {
            const string sql = @"
                SELECT TOP 1 w.ID_Zlecenia, w.PriorytetNum
                FROM Zlecenie_Produkcyjne w
                WHERE w.Status_Zlecenia = 'Wstrzymane' AND w.IsDeleted = 0
                ORDER BY w.PriorytetNum DESC";

            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync(ct);
            if (!await rdr.ReadAsync(ct)) return;
            int idWstrzym  = rdr.GetInt32(0);
            int priorWstrz = rdr.GetInt32(1);
            await rdr.CloseAsync();

            // Sprawdź priorytet aktywnego
            await using var aktCmd = new SqlCommand(
                "SELECT PriorytetNum FROM Zlecenie_Produkcyjne WHERE ID_Zlecenia=@ID", conn);
            aktCmd.Parameters.AddWithValue("@ID", idAktywnego);
            int priorAkt = Convert.ToInt32(await aktCmd.ExecuteScalarAsync() ?? 0);

            if (priorWstrz > priorAkt)
            {
                // Wstrzymaj bieżące, wznów wyższe
                await using var wst = new SqlCommand(
                    "UPDATE Zlecenie_Produkcyjne SET Status_Zlecenia='Wstrzymane' WHERE ID_Zlecenia=@ID",
                    conn);
                wst.Parameters.AddWithValue("@ID", idAktywnego);
                await wst.ExecuteNonQueryAsync(ct);

                await using var wzn = new SqlCommand(
                    "UPDATE Zlecenie_Produkcyjne SET Status_Zlecenia='W toku' WHERE ID_Zlecenia=@ID",
                    conn);
                wzn.Parameters.AddWithValue("@ID", idWstrzym);
                await wzn.ExecuteNonQueryAsync(ct);
            }
        }

        private async Task WznowNastepnePoZakonczeniumAsync(SqlConnection conn, CancellationToken ct)
        {
            // Wznów najwyżej priorytetowe wstrzymane zlecenie
            const string sql = @"
                SELECT TOP 1 ID_Zlecenia
                FROM Zlecenie_Produkcyjne
                WHERE Status_Zlecenia IN ('Wstrzymane','Nowe') AND IsDeleted = 0
                ORDER BY PriorytetNum DESC, DueTime ASC";

            await using var cmd = new SqlCommand(sql, conn);
            var next = await cmd.ExecuteScalarAsync(ct);
            if (next != null)
            {
                await using var upd = new SqlCommand(@"
                    UPDATE Zlecenie_Produkcyjne SET Status_Zlecenia='W toku', StartedAt=GETDATE()
                    WHERE ID_Zlecenia=@ID", conn);
                upd.Parameters.AddWithValue("@ID", next);
                await upd.ExecuteNonQueryAsync(ct);
                _logger.LogInformation("[KOLEJKA] Uruchomiono następne zlecenie {ID}", next);
            }
        }

        private async Task<bool> CzyZlecenieWTokuAsync(int idZlecenia, CancellationToken ct)
        {
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand(
                "SELECT COUNT(1) FROM Zlecenie_Produkcyjne WHERE ID_Zlecenia=@ID AND Status_Zlecenia='W toku'",
                conn);
            cmd.Parameters.AddWithValue("@ID", idZlecenia);
            return (int)(await cmd.ExecuteScalarAsync(ct) ?? 0) > 0;
        }

        private async Task ZapiszPostojBrakZlecenAsync(SqlConnection conn, CancellationToken ct)
        {
            // Zapisz rekord w Realizacja_Produkcji z Kod_Postoju = BRAK-ZLECEN
            const string sql = @"
                INSERT INTO dbo.Realizacja_Produkcji
                    (ID_Zlecenia, ID_Stanowiska, ID_Operatora,
                     Czas_Rozpoczecia, Czas_Zakonczenia,
                     Czas_Splywu_ms, Czas_Cyklu_ms, Czas_Postoju_ms,
                     Ilosc_Wyprodukowanych, Liczba_Wadliwych, Wynik_QC, Kod_Postoju)
                SELECT TOP 1 ID_Zlecenia, 1, 2,
                       GETDATE(), GETDATE(),
                       0, 0, 0, 0, 0, 0, @Kod
                FROM Zlecenie_Produkcyjne
                ORDER BY ID_Zlecenia DESC";

            try
            {
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Kod", KOD_BRAK_ZLECEN);
                await cmd.ExecuteNonQueryAsync(ct);
                _logger.LogWarning("[PRZESTÓJ] Brak aktywnych zleceń — zarejestrowano przestój");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Błąd zapisu przestoju 'Brak zleceń'");
            }
        }
    }
}