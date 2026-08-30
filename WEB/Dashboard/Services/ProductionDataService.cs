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
        /// <summary>
        /// Okno wskaznikow, z ktorego liczy sie kafelek OEE u gory pulpitu. Ten sam
        /// rozmiar okna wykorzystuje trend OEE, zeby jego ostatni punkt zgadzal sie
        /// z liczba na kafelku.
        /// </summary>
        private const int OKNO_KPI = 50;

        /// <summary>
        /// Ostatnio wczytane wartosci dla pulpitu. Serwis jest scoped, czyli zyje
        /// tyle co obwod Blazora i przetrwa przejscia miedzy zakladkami.
        /// Pulpit zaczyna od nich zamiast renderowac zera i podmieniac je sekunde
        /// pozniej - liczniki widzialy taki skok jako wzrost i rozpedzaly sie od zera
        /// przy kazdym powrocie na strone glowna.
        /// </summary>
        public DailyKpi?         OstatnieKpi    { get; set; }
        public ProdukcjaLicznik? OstatniLicznik { get; set; }

        public async Task<DailyKpi> GetDailyKpiAsync(int oknoSztuk = OKNO_KPI)
        {
            var sql = $@"
                ;WITH Ostatnie AS (
                    -- LEFT JOIN, nie INNER: Wskazniki sa teraz dopisywane przez Middleware
                    -- po kazdym cyklu stanowiska i maja ID_Realizacji = NULL (Realizacja_Produkcji
                    -- nie dostaje rekordow). Przy INNER JOIN wypadaly wszystkie co do jednego
                    -- i caly blok OEE stal na zerach.
                    SELECT TOP {oknoSztuk} w.*,
                           ISNULL(r.Ilosc_Wyprodukowanych, 0) AS Ilosc_Wyprodukowanych,
                           ISNULL(r.Liczba_Wadliwych, 0)      AS Liczba_Wadliwych
                    FROM [dbo].[Wskazniki] w
                    LEFT JOIN [dbo].[Realizacja_Produkcji] r ON w.ID_Realizacji = r.ID
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
                    ISNULL(SUM(CASE WHEN ID_Stanowiska = 4 THEN Liczba_Wadliwych ELSE 0 END), 0) AS Wadliwe,
                    COUNT(*) AS LiczbaPomiarow
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
                LiczbaWadliwych  = rdr.GetInt32(7),
                LiczbaPomiarow   = rdr.GetInt32(8)
            };
        }

        // ─────────────────────────────────────────────────────────────
        // Tory z pojemnikami na klocki (stan z HMI, zapisywany przez Middleware)
        // ─────────────────────────────────────────────────────────────
        public async Task<List<KontenerTor>> GetKontenneryAsync()
        {
            const string sql = @"
                SELECT k.ID_Stanowiska, k.NrToru, m.Nazwa_Materialu, ISNULL(m.Kolor,''), k.Wartosc
                FROM Kontenery k
                JOIN Material m ON k.ID_Materialu = m.ID_Materialu
                ORDER BY k.ID_Stanowiska, k.NrToru";

            var result = new List<KontenerTor>();
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                result.Add(new KontenerTor
                {
                    IDStanowiska   = rdr.GetInt32(0),
                    NrToru         = rdr.GetInt32(1),
                    NazwaMaterialu = rdr.GetString(2),
                    Kolor          = rdr.GetString(3),
                    Wartosc        = rdr.GetInt32(4)
                });
            return result;
        }

        // ─────────────────────────────────────────────────────────────
        // Liczniki produkcji: ogolem i "dzisiaj" (od ostatniego resetu zajec).
        // Zrodlem jest DoneAllTime z PLC, zapisywany przez Middleware.
        // Wadliwe = sztuki odrzucone na QC, liczone wzgledem produkcji "dzisiaj".
        // ─────────────────────────────────────────────────────────────
        public async Task<ProdukcjaLicznik> GetLicznikProdukcjiAsync()
        {
            const string sql = @"
                SELECT TOP 1
                    ISNULL(Wyprodukowano_Ogolem, 0),
                    ISNULL(Baseline_Dzisiaj, 0),
                    ISNULL((SELECT SUM(SztukNOK)   FROM Zlecenie_Produkcyjne WHERE IsDeleted = 0), 0),
                    ISNULL((SELECT SUM(SztukOK)    FROM Zlecenie_Produkcyjne WHERE IsDeleted = 0), 0),
                    ISNULL((SELECT SUM(SztukAbort) FROM Zlecenie_Produkcyjne WHERE IsDeleted = 0), 0)
                FROM Ustawienia_Maszyny";

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();

            if (!await rdr.ReadAsync()) return new ProdukcjaLicznik();

            int ogolem   = Convert.ToInt32(rdr[0]);
            int baseline = Convert.ToInt32(rdr[1]);
            int wadliwe  = Convert.ToInt32(rdr[2]);
            int dobre    = Convert.ToInt32(rdr[3]);
            int aborty   = Convert.ToInt32(rdr[4]);

            return new ProdukcjaLicznik
            {
                Ogolem      = ogolem,
                DzisiajZPlc = Math.Max(0, ogolem - baseline),
                Wadliwe     = wadliwe,
                Dobre       = dobre,
                Przerwane   = aborty
            };
        }

        // ─────────────────────────────────────────────────────────────
        // Wydajnosc cyklu per wyrob = suma czasow zadanych / suma czasow rzeczywistych
        // (wszystkie stanowiska razem). LEFT JOIN, zeby ZAWSZE zwrocic komplet wyrobow,
        // takze te bez zarejestrowanej produkcji.
        // ─────────────────────────────────────────────────────────────
        /// <summary>Ile ostatnich cykli wchodzi do wyliczenia "Wydajnosci cyklu" wyrobu.</summary>
        private const int OKNO_CYKLI = 4;

        /// <summary>Ile ostatnich sztuk z QC wchodzi do "Popularnosci wyrobow".</summary>
        private const int OKNO_SZTUK = 50;

        public async Task<List<WyrobCzasCyklu>> GetAvgCycleTimePerWyrobAsync()
        {
            // Okno kroczace: liczy sie tylko OKNO_CYKLI ostatnich cykli danego wyrobu,
            // dzieki czemu kafelek pokazuje biezaca formę linii, a nie srednia od poczatku
            // swiata. Zrodlo to suma biezacych zajec (Realizacja_Produkcji) i trwalego
            // archiwum (HistoriaCykli) - reset zajec nie zeruje wiec tego bloku.
            string sql = $@"
                WITH Cykle AS (
                    SELECT zp.ID_Wyrobu,
                           CAST(r.Czas_Cyklu_ms AS FLOAT)             AS Rzeczywisty,
                           CAST(ISNULL(pm.Czas_Jednostkowy,0) AS FLOAT) AS Zadany,
                           r.Czas_Zakonczenia
                    FROM [dbo].[Realizacja_Produkcji] r
                    JOIN [dbo].[Zlecenie_Produkcyjne] zp ON zp.ID_Zlecenia = r.ID_Zlecenia
                    LEFT JOIN [dbo].[Proces_Montazu]  pm ON pm.ID_Wyrobu   = zp.ID_Wyrobu
                                                          AND pm.ID_Stanowiska = r.ID_Stanowiska
                    WHERE r.Czas_Cyklu_ms > 0
                    UNION ALL
                    SELECT h.ID_Wyrobu,
                           CAST(h.Czas_Cyklu_ms  AS FLOAT),
                           CAST(h.Czas_Zadany_ms AS FLOAT),
                           h.Czas_Zakonczenia
                    FROM [dbo].[HistoriaCykli] h
                    WHERE h.Czas_Cyklu_ms > 0
                ),
                Okno AS (
                    SELECT *, ROW_NUMBER() OVER (PARTITION BY ID_Wyrobu
                                                 ORDER BY Czas_Zakonczenia DESC) AS Lp
                    FROM Cykle
                )
                SELECT w.Nazwa_Wyrobu,
                       ISNULL(SUM(o.Rzeczywisty), 0) AS SumaRzeczywista,
                       ISNULL(SUM(o.Zadany), 0)      AS SumaZadana,
                       COUNT(o.ID_Wyrobu)            AS LiczbaCykli
                FROM [dbo].[Wyrob] w
                LEFT JOIN Okno o ON o.ID_Wyrobu = w.ID_Wyrobu AND o.Lp <= {OKNO_CYKLI}
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
                int    liczba      = rdr.GetInt32(3);
                result.Add(new WyrobCzasCyklu
                {
                    Nazwa        = rdr.GetString(0),
                    SumaCzasMs   = rzeczywista,
                    LiczbaCykli  = liczba,
                    SredniCyklMs = liczba > 0 ? rzeczywista / liczba : 0,
                    Wydajnosc    = rzeczywista > 0 ? zadana / rzeczywista : (double?)null
                });
            }
            return result;
        }

        // ─────────────────────────────────────────────────────────────
        // Popularnosc wyrobow (% udzialu w ukonczonych sztukach - QC, stanowisko 4)
        // ─────────────────────────────────────────────────────────────
        public async Task<List<WyrobPopularnosc>> GetWyrobPopularnosciAsync()
        {
            // Tak jak wydajnosc cyklu: okno kroczace OKNO_SZTUK ostatnich sztuk z QC
            // (stanowisko 4), liczone z biezacych zajec i trwalego archiwum razem.
            // Reset zajec nie zeruje wiec tego bloku.
            string sql = $@"
                WITH Sztuki AS (
                    SELECT zp.ID_Wyrobu, r.Czas_Zakonczenia
                    FROM [dbo].[Realizacja_Produkcji] r
                    JOIN [dbo].[Zlecenie_Produkcyjne] zp ON r.ID_Zlecenia = zp.ID_Zlecenia
                    WHERE r.ID_Stanowiska = 4
                    UNION ALL
                    SELECT h.ID_Wyrobu, h.Czas_Zakonczenia
                    FROM [dbo].[HistoriaCykli] h
                    WHERE h.ID_Stanowiska = 4
                ),
                Okno AS (
                    SELECT TOP ({OKNO_SZTUK}) ID_Wyrobu
                    FROM Sztuki
                    ORDER BY Czas_Zakonczenia DESC
                )
                -- LEFT JOIN od strony Wyrob: blok ma wypisywac KOMPLET wyrobow,
                -- a te bez produkcji maja pokazac 0%, nie znikac z listy.
                SELECT w.Nazwa_Wyrobu, COUNT(o.ID_Wyrobu) AS Ilosc
                FROM [dbo].[Wyrob] w
                LEFT JOIN Okno o ON o.ID_Wyrobu = w.ID_Wyrobu
                GROUP BY w.ID_Wyrobu, w.Nazwa_Wyrobu
                ORDER BY COUNT(o.ID_Wyrobu) DESC, w.ID_Wyrobu";

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
        /// <summary>
        /// Okno cykli do wydajnosci stanowiska. MUSI byc rowne OKNO_CYKLI_HMI
        /// w DatabaseHelper Middleware - obie strony maja pokazywac te sama liczbe.
        /// </summary>
        private const int OKNO_CYKLI_STANOWISKA = 4;

        public async Task<List<StanowiskoStatus>> GetStanowiskaStatusAsync()
        {
            var sql = $@"
                SELECT
                    s.ID_Stanowiska,
                    s.Nazwa_Stanowiska,
                    ISNULL(s.Stan_Produkcji, 0) AS StanProdukcji,
                    s.Stan_Aktualizacja,
                    w.Wskaznik_OEE,
                    r.Czas_Cyklu_ms,
                    r.Czas_Zakonczenia,
                    r.Kod_Postoju,
                    -- ISNULL(..., so.*) - gdy biezacych danych nie ma (np. po
                    -- 'Rozpocznij nowe zajecia' zlecenia sa skasowane), karta pokazuje
                    -- ostatni wyrob z trwalej pamieci StanowiskoOstatnie zamiast myslnikow.
                    ISNULL(zp.Nazwa_Zlecenia,      so.Nazwa_Zlecenia),
                    ISNULL(wy.Nazwa_Wyrobu,        so.Nazwa_Wyrobu),
                    ISNULL(pmLast.Czas_Jednostkowy, so.Czas_Zadany_ms),
                    perf.SumaZadana,
                    perf.SumaRzeczywista,
                    -- Zmierzony czas ostatniego cyklu. Pierwszenstwo ma HistoriaCykli,
                    -- bo tam trafia pomiar z Middleware (przejscia Production.State),
                    -- a nie oszacowanie liczone zegarem przegladarki.
                    ISNULL(ost.Czas_Cyklu_ms, ISNULL(r.Czas_Cyklu_ms, so.Czas_Cyklu_ms)) AS CyklZPamiecia,
                    ost.Czas_Zakonczenia AS CyklZarejestrowano,
                    so.Wydajnosc                                     AS WydajnoscZPamieci
                FROM [dbo].[Stanowisko] s
                LEFT JOIN [dbo].[StanowiskoOstatnie] so ON so.ID_Stanowiska = s.ID_Stanowiska
                -- Ostatni FAKTYCZNIE zmierzony cykl tego stanowiska.
                OUTER APPLY (
                    SELECT TOP 1 hc.Czas_Cyklu_ms, hc.Czas_Zakonczenia
                    FROM [dbo].[HistoriaCykli] hc
                    WHERE hc.ID_Stanowiska = s.ID_Stanowiska AND hc.Czas_Cyklu_ms > 0
                    ORDER BY hc.ID DESC
                ) ost
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
                LEFT JOIN [dbo].[Wskazniki] w ON r.ID = w.ID_Realizacji
                -- Zlecenie bierzemy z pola Nr_Zlecenia TEGO stanowiska (Production.OrderNo
                -- z PLC). Wczesniej bylo tu pierwsze aktywne zlecenie z brzegu, wiec przy
                -- dwoch sztukach jadacych rownolegle wszystkie cztery karty pokazywaly to
                -- samo zlecenie - takze na stanowiskach, ktore mialy u siebie zupelnie inna.
                -- Fallback na ostatnie aktywne tylko wtedy, gdy PLC nie podal numeru.
                OUTER APPLY (
                    SELECT TOP 1 z.Nazwa_Zlecenia, z.ID_Wyrobu
                    FROM [dbo].[Zlecenie_Produkcyjne] z
                    WHERE z.IsDeleted = 0
                      AND (z.ID_Zlecenia = s.Nr_Zlecenia
                           OR (s.Nr_Zlecenia IS NULL AND z.Status_Zlecenia IN ('W toku','Nowe')))
                    ORDER BY CASE WHEN z.ID_Zlecenia = s.Nr_Zlecenia THEN 0 ELSE 1 END,
                             CASE WHEN z.Status_Zlecenia = 'W toku' THEN 0 ELSE 1 END,
                             z.PriorytetNum DESC, z.ID_Zlecenia DESC
                ) zp
                LEFT JOIN [dbo].[Wyrob] wy ON zp.ID_Wyrobu = wy.ID_Wyrobu
                LEFT JOIN [dbo].[Proces_Montazu] pmLast ON pmLast.ID_Wyrobu = zp.ID_Wyrobu
                                                        AND pmLast.ID_Stanowiska = s.ID_Stanowiska
                -- Wydajnosc stanowiska liczymy z HistoriaCykli, czyli DOKLADNIE z tego
                -- samego okna co wartosc wysylana na panel HMI (Stats.Efficiency) -
                -- inaczej operator widzialby na swoim panelu inna liczbe niz pulpit.
                -- Realizacja_Produkcji sie tu nie nadaje: nie dostaje rekordow.
                OUTER APPLY (
                    SELECT SUM(CAST(o.Czas_Zadany_ms AS float)) AS SumaZadana,
                           SUM(CAST(o.Czas_Cyklu_ms  AS float)) AS SumaRzeczywista
                    FROM (
                        SELECT TOP ({OKNO_CYKLI_STANOWISKA}) hc.Czas_Zadany_ms, hc.Czas_Cyklu_ms
                        FROM [dbo].[HistoriaCykli] hc
                        WHERE hc.ID_Stanowiska = s.ID_Stanowiska
                          AND hc.Czas_Cyklu_ms > 0 AND hc.Czas_Zadany_ms > 0
                        ORDER BY hc.ID DESC
                    ) o
                ) perf
                ORDER BY s.ID_Stanowiska";

            var result = new List<StanowiskoStatus>();
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
            {
                double? sumaZadana       = rdr.IsDBNull(11) ? null : Convert.ToDouble(rdr[11]);
                double? sumaRzeczywista  = rdr.IsDBNull(12) ? null : Convert.ToDouble(rdr[12]);

                result.Add(new StanowiskoStatus
                {
                    IDStanowiska   = rdr.GetInt32(0),
                    Nazwa          = rdr.GetString(1),
                    StanProdukcji  = Convert.ToInt32(rdr[2]),
                    StanOd         = rdr.IsDBNull(3) ? null : rdr.GetDateTime(3),
                    OEE            = rdr.IsDBNull(4) ? null : (double?)Convert.ToDouble(rdr[4]),
                    OstatniCyklMs  = rdr.IsDBNull(13) ? null : Convert.ToInt32(rdr[13]),
                    OstatniaCzas   = rdr.IsDBNull(6) ? null : rdr.GetDateTime(6),
                    KodPostoju     = rdr.IsDBNull(7) ? null : rdr.GetString(7),
                    NazwaZlecenia  = rdr.IsDBNull(8) ? null : rdr.GetString(8),
                    NazwaWyrobu    = rdr.IsDBNull(9) ? null : rdr.GetString(9),
                    OstatniCzasZadanyMs = rdr.IsDBNull(10) ? null : (int?)Convert.ToInt32(rdr[10]),
                    OstatniCyklCzas = rdr.IsDBNull(14) ? null : rdr.GetDateTime(14),
                    Wydajnosc      = (sumaZadana.HasValue && sumaRzeczywista is > 0)
                                        ? sumaZadana.Value / sumaRzeczywista.Value
                                        : (rdr.IsDBNull(15) ? null : Convert.ToDouble(rdr[15]))
                });
            }
            return result;
        }

        // Ostatnio zapisana migawka - zeby nie strzelac UPDATE-em przy kazdym
        // odswiezeniu pulpitu, gdy nic sie nie zmienilo.
        private static readonly Dictionary<int, string> _ostatnioZapisane = new();

        /// <summary>
        /// Utrwala "co ostatnio bylo na stanowisku". Wolane z wolnej petli pulpitu.
        /// Dzieki temu karty stanowisk nie pustoszeja po przeladowaniu strony ani po
        /// "Rozpocznij nowe zajecia" - tabela StanowiskoOstatnie nie jest resetowana.
        /// </summary>
        public async Task ZapamietajStanowiskaAsync(List<StanowiskoStatus> stanowiska)
        {
            var doZapisu = new List<StanowiskoStatus>();
            foreach (var s in stanowiska)
            {
                // Nie nadpisujemy pamieci pustka - inaczej pierwszy odczyt bez danych
                // wymazalby to, co chcemy zachowac.
                if (s.NazwaZlecenia is null && s.NazwaWyrobu is null) continue;

                var odcisk = $"{s.NazwaZlecenia}|{s.NazwaWyrobu}|{s.OstatniCyklMs}|{s.OstatniCzasZadanyMs}|{s.Wydajnosc:F4}";
                if (_ostatnioZapisane.TryGetValue(s.IDStanowiska, out var poprzedni) && poprzedni == odcisk)
                    continue;

                _ostatnioZapisane[s.IDStanowiska] = odcisk;
                doZapisu.Add(s);
            }
            if (doZapisu.Count == 0) return;

            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            foreach (var s in doZapisu)
            {
                await using var cmd = new SqlCommand(@"
                    MERGE dbo.StanowiskoOstatnie AS c
                    USING (SELECT @St AS ID_Stanowiska) AS n ON c.ID_Stanowiska = n.ID_Stanowiska
                    WHEN MATCHED THEN UPDATE SET
                        Nazwa_Zlecenia = @Zl, Nazwa_Wyrobu = @Wy, Czas_Cyklu_ms = @Cykl,
                        Czas_Zadany_ms = @Zad, Wydajnosc = @Wyd, Zaktualizowano = GETDATE()
                    WHEN NOT MATCHED THEN INSERT
                        (ID_Stanowiska, Nazwa_Zlecenia, Nazwa_Wyrobu, Czas_Cyklu_ms, Czas_Zadany_ms, Wydajnosc)
                        VALUES (@St, @Zl, @Wy, @Cykl, @Zad, @Wyd);", conn);
                cmd.Parameters.AddWithValue("@St",   s.IDStanowiska);
                cmd.Parameters.AddWithValue("@Zl",   (object?)s.NazwaZlecenia ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Wy",   (object?)s.NazwaWyrobu ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Cykl", (object?)s.OstatniCyklMs ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Zad",  (object?)s.OstatniCzasZadanyMs ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@Wyd",  (object?)s.Wydajnosc ?? DBNull.Value);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        // ─────────────────────────────────────────────────────────────
        // Trend OEE — ostatnie N cykli (wszystkie stanowiska)
        // ─────────────────────────────────────────────────────────────
        public async Task<List<OeeTrendPoint>> GetOeeTrendAsync(int n = 30)
        {
            // JEDEN ogolny trend, nie linia na stanowisko. Kazdy punkt to srednia kroczaca
            // z tego samego okna OKNO_KPI co kafelek OEE u gory - dzieki temu ostatni punkt
            // wykresu rowna sie liczbie na kafelku, a wykres pokazuje, jak ona doszla do tej
            // wartosci. ID w ORDER BY rozstrzyga remisy czasu (4 stanowiska zapisuja sie
            // w tej samej sekundzie).
            var sql = $@"
                WITH Kolejno AS (
                    SELECT w.DataCzas_Pomiaru,
                           AVG(CAST(w.Wskaznik_OEE AS FLOAT)) OVER (ORDER BY w.DataCzas_Pomiaru, w.ID
                                ROWS BETWEEN {OKNO_KPI - 1} PRECEDING AND CURRENT ROW) AS OEE,
                           AVG(CAST(w.Dostepnosc   AS FLOAT)) OVER (ORDER BY w.DataCzas_Pomiaru, w.ID
                                ROWS BETWEEN {OKNO_KPI - 1} PRECEDING AND CURRENT ROW) AS A,
                           AVG(CAST(w.Wydajnosc    AS FLOAT)) OVER (ORDER BY w.DataCzas_Pomiaru, w.ID
                                ROWS BETWEEN {OKNO_KPI - 1} PRECEDING AND CURRENT ROW) AS P,
                           AVG(CAST(w.Jakosc       AS FLOAT)) OVER (ORDER BY w.DataCzas_Pomiaru, w.ID
                                ROWS BETWEEN {OKNO_KPI - 1} PRECEDING AND CURRENT ROW) AS Q,
                           ROW_NUMBER() OVER (ORDER BY w.DataCzas_Pomiaru DESC, w.ID DESC) AS Lp
                    FROM [dbo].[Wskazniki] w
                )
                SELECT DataCzas_Pomiaru, N'OEE', OEE, A, P, Q
                FROM Kolejno
                WHERE Lp <= {n}
                ORDER BY DataCzas_Pomiaru DESC, Lp";

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
            // Liczniki bierzemy PROSTO ze zlecenia (SztukOK/SztukNOK), a nie z
            // Realizacja_Produkcji - tamta tabela nie dostaje rekordow, wiec pulpit
            // pokazywal zawsze 0 wyprodukowanych. Dochodzi tez filtr IsDeleted,
            // ktorego tu brakowalo: skasowane zlecenia wisialy na liscie.
            const string sql = @"
                SELECT zp.ID_Zlecenia, zp.Nazwa_Zlecenia, zp.Ilosc_Sztuk,
                       ISNULL(zp.SztukOK, 0), ISNULL(zp.SztukNOK, 0),
                       zp.Data_Realizacji, zp.Status_Zlecenia,
                       ISNULL(zp.Czas_Planowany_ms, 0),
                       w.Nazwa_Wyrobu,
                       ISNULL(zp.Priorytet, 'Standardowy'),
                       zp.StartedAt, zp.CompletedAt
                FROM [dbo].[Zlecenie_Produkcyjne] zp
                LEFT JOIN [dbo].[Wyrob] w ON zp.ID_Wyrobu = w.ID_Wyrobu
                WHERE zp.IsDeleted = 0
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
                    SztukOK         = rdr.GetInt32(3),
                    Wyprodukowano   = rdr.GetInt32(3),
                    SztukNOK        = rdr.GetInt32(4),
                    DataRealizacji  = rdr.IsDBNull(5) ? null : rdr.GetDateTime(5),
                    StatusZlecenia  = rdr.GetString(6),
                    CzasPlanowanyMs = rdr.GetInt32(7),
                    NazwaWyrobu     = rdr.IsDBNull(8) ? null : rdr.GetString(8),
                    Priorytet       = rdr.GetString(9),
                    StartedAt       = rdr.IsDBNull(10) ? null : rdr.GetDateTime(10),
                    CompletedAt     = rdr.IsDBNull(11) ? null : rdr.GetDateTime(11)
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
