using System.Text;

namespace PlcToDbMiddleware
{
    class Program
    {
        // ================================================================
        //  KONFIGURACJA
        // ================================================================

        /// <summary>
        ///  TRUE  = tryb symulacji (bez PLC, dane losowe) — do testowania bazy
        ///  FALSE = tryb produkcyjny (wymaga polaczenia z PLC)
        /// </summary>
        const bool SIMULATION_MODE = false;

        const int SIMULATION_INTERVAL_MS = 4000;

        static readonly string PlcIpAddress = "192.168.1.1";

        static readonly string ConnectionString =
            @"Data Source=(localdb)\MSSQLLocalDB;" +
            @"Initial Catalog=BazaDanychRB;" +
            @"Integrated Security=True;" +
            @"Connect Timeout=30;" +
            @"Encrypt=True;" +
            @"Trust Server Certificate=False;" +
            @"Application Intent=ReadWrite;" +
            @"Multi Subnet Failover=False;" +
            @"Command Timeout=30";

        const int POLL_INTERVAL_MS = 200;
        // ================================================================

        static void Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.Title = "PlcToDb Middleware v2.0";

            // Dwie kopie naraz pisalyby do tego samego PLC i bilyby sie o LocalDB.
            using var jedynaInstancja = new Mutex(true, @"Global\LiniaMontazowa_Middleware", out bool pierwszy);
            if (!pierwszy)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Middleware juz dziala (inna instancja). Uruchamiam tylko jedna kopie.");
                Console.ResetColor();
                return;
            }

            PrintHeader();

            var db = new DatabaseHelper(ConnectionString);

            if (SIMULATION_MODE)
                RunSimulation(db);
            else
                RunWithPlc(db);
        }

        // ================================================================
        //  TRYB SYMULACJI — bez PLC
        // ================================================================
        static void RunSimulation(DatabaseHelper db)
        {
            var rng = new Random();

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("╔══════════════════════════════════════════════════╗");
            Console.WriteLine("║  TRYB SYMULACJI — dane losowe, bez PLC           ║");
            Console.WriteLine($"║  Nowy cykl co {SIMULATION_INTERVAL_MS / 1000} sekundy. Ctrl+C = stop.          ║");
            Console.WriteLine("╚══════════════════════════════════════════════════╝");
            Console.ResetColor();

            // Nazwy musza istniec w bazie danych!
            string[] stanowiska = {
                "Stanowisko Montaz 1",
                "Stanowisko Montaz 2",
                "Stanowisko Montaz 3",
                "Stanowisko QC"
            };

            Console.Write("[SIM] Podaj nazwe zlecenia (musi istniec w tabeli Zlecenie_Produkcyjne): ");
            string simZlecenie = Console.ReadLine() ?? "ZL-001";
            if (string.IsNullOrWhiteSpace(simZlecenie)) simZlecenie = "ZL-001";

            Console.WriteLine($"\n[SIM] Start. Zlecenie='{simZlecenie}'\n");

            DateTime lastTrigger = DateTime.Now;

            while (true)
            {
                Thread.Sleep(SIMULATION_INTERVAL_MS);

                DateTime now  = DateTime.Now;
                int splywMs   = (int)(now - lastTrigger).TotalMilliseconds;

                string stanowisko    = stanowiska[rng.Next(stanowiska.Length)];
                int czasCykluMs      = rng.Next(2500, Math.Max(2600, splywMs - 100));
                int czasPlanowyMs    = 3500;
                int iloscWyprod      = 1;
                int liczbaWad        = rng.Next(100) < 10 ? 1 : 0;
                int postoiMs         = Math.Max(0, splywMs - czasCykluMs);
                string? kodPostoju   = postoiMs > 600
                                           ? (rng.Next(2) == 0 ? "AWARIA" : "PRZERWA")
                                           : null;

                try
                {
                    var stan = db.GetStanowiskoByName(stanowisko);
                    var op   = db.GetSystemOperator();
                    var zl   = db.GetZlecenieByName(simZlecenie);

                    var data = BuildPlcData(zl, stan, op, czasCykluMs, czasPlanowyMs,
                                           iloscWyprod, liczbaWad, kodPostoju,
                                           liczbaWad == 0, lastTrigger, now, splywMs, postoiMs);

                    Console.WriteLine($"┌─ [{now:HH:mm:ss}] SIM — cykl symulowany");
                    PrintCycleSummary(data, stan, op, zl);
                    SaveToDatabase(db, data, stan, op);
                    db.IncrementRozpoczeteSztuki(zl.ID);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[BLAD] {ex.Message}");
                    Console.ResetColor();
                }

                lastTrigger = now;
            }
        }

        // ================================================================
        //  TRYB PRODUKCYJNY — prawdziwy PLC
        // ================================================================
        static bool _zglosilBrakZlecen;
        static DateTime _ostatnieSzukanieKolejki = DateTime.MinValue;

        /// <summary>
        /// Podaje PLC kolejne zlecenie z kolejki.
        ///
        /// Zasada: PLC samo zarzadza slotem NastepneZlecenie - zmniejsza PartNo po kazdej
        /// sztuce, a po ostatniej zeruje caly slot (NastepneZlecenie := PusteZlecenie).
        /// Pusty slot to wiec sygnal "linia gotowa na nastepne zlecenie".
        ///
        /// Zlecenie zostaje w SQL jako aktywne dopoki sztuki nie przejda przez QC, dlatego
        /// nie wystarczy wziac pierwsze z brzegu - trzeba pominac te, ktore w calosci
        /// poszly juz na linie, inaczej to samo zlecenie produkowaloby sie w kolko.
        /// </summary>
        static void SyncActiveOrderToPlc(PlcReader plc, DatabaseHelper db) {
            try {
                // Slot zajety - PLC pracuje, nie ma czego podawac.
                if (plc.ReadNastepneZlecenieId() != 0) { _zglosilBrakZlecen = false; return; }

                var activeOrders = db.GetActiveOrders();
                if (activeOrders.Count == 0) {
                    if (!_zglosilBrakZlecen) {
                        Console.WriteLine("[INFO] Kolejka pusta - brak zlecen do wyslania.");
                        _zglosilBrakZlecen = true;
                    }
                    return;
                }

                // Skan tablicy (28 kB) tylko co 2 s - slot potrafi byc pusty przez dluzszy czas.
                if ((DateTime.Now - _ostatnieSzukanieKolejki).TotalMilliseconds < 2000) return;
                _ostatnieSzukanieKolejki = DateTime.Now;

                var naLinii = plc.LiczSztukiWszystkichZlecen();
                if (naLinii == null) return;   // blad odczytu - sprobujemy za chwile

                // activeOrders jest juz posortowane wg priorytetu i terminu.
                foreach (var z in activeOrders) {
                    int juz = naLinii.TryGetValue(z.id, out var n) ? n : 0;
                    int pozostalo = z.iloscSztuk - juz;
                    if (pozostalo <= 0) continue;   // to zlecenie w calosci poszlo juz na linie

                    plc.WriteOrderToPlc(z.id, z.idWyrobu + 1, pozostalo, z.priority);
                    Console.WriteLine($"[INFO] Zlecenie -> PLC: ID={z.id} PartNo={pozostalo}" +
                                      (juz > 0 ? $" (wznowienie, {juz} juz na linii)" : ""));
                    _zglosilBrakZlecen = false;
                    return;
                }

                if (!_zglosilBrakZlecen) {
                    Console.WriteLine("[INFO] Wszystkie aktywne zlecenia sa juz na linii - czekam na QC.");
                    _zglosilBrakZlecen = true;
                }
            } catch (Exception ex) {
                Console.WriteLine($"[WARN] Nie udalo sie zsynchronizowac: {ex.Message}");
            }
        }

        // Abort dotyczy TYLKO jednej sztuki (nie calego zlecenia) - wiec nie anulujemy
        // zlecenia automatycznie, tylko zglaszamy powiadomienie. Dedupe trwale w bazie
        // (PowiadomieniaAbortProcessed), zeby przetrwalo restart Middleware.
        static void CheckAborts(PlcReader plc, DatabaseHelper db) {
            try {
                var events = plc.ReadAbortEvents();
                foreach (var (slot, idZlecenia, stanowiskoNr) in events) {
                    try { db.ZapiszPowiadomienieAbortu(slot, idZlecenia, stanowiskoNr); }
                    catch (Exception ex) { Console.WriteLine($"[WARN] Nie udalo sie zapisac powiadomienia (zlecenie {idZlecenia}): {ex.Message}"); }
                }
            } catch (Exception ex) {
                Console.WriteLine($"[WARN] Nie udalo sie sprawdzic Abort z PLC: {ex.Message}");
            }
        }

        // Gdy operator nacisnie Start na Stanowisku 1 (Production.State -> 1),
        // odpowiadajace zlecenie przechodzi 'Nowe' -> 'W toku'.
        // Dane pochodza z gotowej migawki - osobny odczyt z PLC kosztowal ok. 70 ms na cykl.
        static int _ostatnieZgloszoneStartem = -1;

        static void CheckOrderStarted(DatabaseHelper db, int stan, int orderNo) {
            try {
                if (stan != 1 || orderNo <= 0) return;
                if (orderNo == _ostatnieZgloszoneStartem) return;   // juz oznaczone
                db.MarkOrderStartedIfNew(orderNo);
                _ostatnieZgloszoneStartem = orderNo;
            } catch (Exception ex) {
                Console.WriteLine($"[WARN] Nie udalo sie sprawdzic startu zlecenia: {ex.Message}");
            }
        }

        static readonly int[] _ostatniStanStanowiska = { -1, -1, -1, -1 };

        // Pomiar czasu pracy stanowiska: moment wejscia w stan 1 i numer zlecenia,
        // ktore wtedy na nim bylo. Realizacja_Produkcji nie dostaje rekordow
        // (martwy wyzwalacz z DB5), wiec to jedyne zrodlo rzeczywistych czasow cyklu.
        static readonly DateTime[] _startPracy   = new DateTime[4];
        static readonly int[]      _zlecenieStartu = { 0, 0, 0, 0 };

        // Cykl krotszy niz to uznajemy za drgniecie sygnalu, dluzszy za przerwe
        // (ktos zostawil stanowisko w stanie pracy) - w obu wypadkach nie zapisujemy.
        const int MIN_CYKL_MS = 700;
        const int MAX_CYKL_MS = 15 * 60 * 1000;

        /// <summary>
        /// Reaguje na zmiane Production.State: wejscie w 1 startuje stoper, wyjscie z 1
        /// zamyka cykl, zapisuje go i odsyla swieza wydajnosc na panel HMI stanowiska.
        /// </summary>
        static void ObsluzPomiarCyklu(PlcReader plc, DatabaseHelper db,
                                      int nr, int poprzedni, int stan, int nrZlecenia)
        {
            if (stan == 1) {                       // stanowisko wlasnie ruszylo
                _startPracy[nr - 1]     = DateTime.Now;
                _zlecenieStartu[nr - 1] = nrZlecenia;
                return;
            }

            if (poprzedni != 1) return;            // nie wychodzimy z pracy - nie ma co mierzyc
            if (_startPracy[nr - 1] == default) return;

            int czasMs = (int)(DateTime.Now - _startPracy[nr - 1]).TotalMilliseconds;
            _startPracy[nr - 1] = default;

            if (czasMs < MIN_CYKL_MS || czasMs > MAX_CYKL_MS) {
                Console.WriteLine($"[INFO] Stanowisko {nr}: cykl {czasMs} ms poza zakresem - pomijam.");
                return;
            }

            // Numer zlecenia bierzemy z chwili STARTU - przy wyjsciu ze stanu pracy
            // PLC potrafi juz wyzerowac OrderNo.
            int zlecenie = _zlecenieStartu[nr - 1] != 0 ? _zlecenieStartu[nr - 1] : nrZlecenia;
            if (zlecenie == 0) return;

            // PLC mierzy czas u siebie (udtZlecenia.Time.StanowiskoX.ActualTime, sekundy)
            // - to wartosc, ktora operator widzi na HMI, wiec ma pierwszenstwo przed
            // stoperem Middleware. Stoper zostaje jako kontrola zdrowego rozsadku:
            // gdy oba wyniki mocno sie rozjezdzaja, ufamy wlasnemu i zglaszamy to w logu.
            string zrodlo = "stoper";
            var czasyPlc = plc.ReadCzasyStanowiska(zlecenie, nr);
            if (czasyPlc is { ActualSek: > 0 }) {
                int zPlcMs = czasyPlc.ActualSek * 1000;
                double stosunek = (double)zPlcMs / czasMs;
                if (stosunek is > 0.3 and < 3.0) {
                    czasMs = zPlcMs;
                    zrodlo = "PLC";
                } else {
                    Console.WriteLine($"[WARN] Stanowisko {nr}: ActualTime z PLC = {czasyPlc.ActualSek} s "
                                    + $"vs stoper {czasMs / 1000.0:0.0} s - rozbieznosc x{stosunek:0.00}, "
                                    + "biore stoper (sprawdz jednostke ActualTime w TIA).");
                }
            }

            try {
                int? wydajnosc = db.ZapiszCyklStanowiska(nr, zlecenie, czasMs);
                Console.WriteLine($"[INFO] Stanowisko {nr}: cykl {czasMs / 1000.0:0.0} s ({zrodlo}, zlecenie {zlecenie})"
                                + (wydajnosc.HasValue ? $", wydajnosc {wydajnosc}%" : ", brak normy - wydajnosc pominieta"));

                if (wydajnosc.HasValue) {
                    plc.WriteEfficiency(nr, wydajnosc.Value);
                }
            } catch (Exception ex) {
                Console.WriteLine($"[WARN] Stanowisko {nr}: nie udalo sie zapisac cyklu: {ex.Message}");
            }
        }
        static readonly int[,] _ostatnieKontenery2 = { { -2, -2, -2 }, { -2, -2, -2 }, { -2, -2, -2 } };

        /// <summary>
        /// Jedna migawka DB_Data -> stany stanowisk + kontenery. Wszystko w jednym
        /// odczycie z PLC, zamiast kilkunastu osobnych zapytan.
        /// </summary>
        static void SyncStanowiskaIKontenery(PlcReader plc, DatabaseHelper db) {
            try {
                var m = plc.ReadMigawkaStanowisk();
                if (m == null) return;

                // Start zlecenia wykrywamy z tej samej migawki - bez dodatkowego odczytu.
                CheckOrderStarted(db, m.Stany[0], m.NumeryZlecen[0]);

                for (int nr = 1; nr <= 4; nr++) {
                    int stan = m.Stany[nr - 1];
                    if (stan == _ostatniStanStanowiska[nr - 1]) continue;

                    int poprzedni = _ostatniStanStanowiska[nr - 1];
                    db.ZapiszStanStanowiska(nr, stan);
                    Console.WriteLine($"[INFO] Stanowisko {nr}: stan {poprzedni} -> {stan} (zlecenie {m.NumeryZlecen[nr - 1]})");
                    _ostatniStanStanowiska[nr - 1] = stan;

                    ObsluzPomiarCyklu(plc, db, nr, poprzedni, stan, m.NumeryZlecen[nr - 1]);
                }

                for (int st = 1; st <= 3; st++) {
                    for (int tor = 1; tor <= 3; tor++) {
                        int nowa = m.Kontenery[st - 1, tor - 1];
                        int stara = _ostatnieKontenery2[st - 1, tor - 1];
                        if (nowa == stara) continue;

                        // PLC potrafi zejsc ponizej zera (blokada Subtract.Enable nie chroni
                        // samego odejmowania), a ujemna liczba pojemnikow nie ma sensu.
                        if (nowa < 0) {
                            Console.WriteLine($"[WARN] Stanowisko {st}, tor {tor}: PLC zglasza {nowa} pojemnikow - traktuje jako 0.");
                            nowa = 0;
                        }

                        db.ZapiszKontener(st, tor, nowa, stara < 0 ? -1 : stara);
                        _ostatnieKontenery2[st - 1, tor - 1] = m.Kontenery[st - 1, tor - 1];
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine($"[WARN] Nie udalo sie odczytac stanowisk/kontenerow: {ex.Message}");
            }
        }

        // Sztuki po QC z tablicy Zlecenie[] -> SztukOK/SztukNOK zlecenia.
        // Zastepuje martwy mechanizm oparty na wyzwalaczu z DB5 (nigdy nie wypelnial danych).
        static void SyncWynikiQC(PlcReader plc, DatabaseHelper db) {
            try {
                foreach (var s in plc.ReadSztukiPoQC()) {
                    try { db.ZarejestrujSztukePoQC(s.IdZlecenia, s.PartNo, s.WynikOK); }
                    catch (Exception ex) { Console.WriteLine($"[WARN] QC zlecenie {s.IdZlecenia}/{s.PartNo}: {ex.Message}"); }
                }
            } catch (Exception ex) {
                Console.WriteLine($"[WARN] Nie udalo sie zsynchronizowac wynikow QC: {ex.Message}");
            }
        }


        static int _ostatniaSumaDone = -1;

        // Licznik DoneAllTime z PLC -> SQL, zeby Dashboard mogl pokazac produkcje
        // ogolem i "dzisiaj" (roznica wzgledem baseline z ostatniego resetu zajec).
        static void SyncLicznikProdukcji(PlcReader plc, DatabaseHelper db) {
            try {
                int suma = plc.ReadWyprodukowaneWyroby();
                if (suma < 0 || suma == _ostatniaSumaDone) return;
                db.ZapiszWyprodukowanoOgolem(suma);
                _ostatniaSumaDone = suma;
            } catch (Exception ex) {
                Console.WriteLine($"[WARN] Nie udalo sie zapisac licznika produkcji: {ex.Message}");
            }
        }

        /// <summary>
        /// Zdejmuje z DB3 rekordy zlecen zamknietych na stronie. PLC nie robi tego sam -
        /// slot zostawal z ID != 0 i ustawionym bitem QC, wiec linia dalej widziala
        /// nieaktualne zlecenie. Znacznik PlcWyczyszczone pilnuje, zeby zrobic to raz.
        /// </summary>
        static void SprzatnijZakonczoneZlecenia(PlcReader plc, DatabaseHelper db) {
            try {
                foreach (int id in db.GetZleceniaDoWyczyszczeniaWPlc()) {
                    try {
                        int slot = plc.ClearOrderSlot(id);
                        plc.ClearNastepneZlecenieIf(id);
                        db.OznaczPlcWyczyszczone(id);
                        Console.WriteLine(slot >= 0
                            ? $"[INFO] Zlecenie {id} zamkniete na stronie -> wyzerowano Zlecenie[{slot}] w DB3."
                            : $"[INFO] Zlecenie {id} zamkniete na stronie -> brak wpisu w DB3, oznaczam jako posprzatane.");
                    } catch (Exception ex) {
                        Console.WriteLine($"[WARN] Nie udalo sie wyczyscic zlecenia {id} w PLC: {ex.Message}");
                    }
                }
            } catch (Exception ex) {
                Console.WriteLine($"[WARN] Nie udalo sie sprawdzic zlecen do wyczyszczenia: {ex.Message}");
            }
        }

        static void CheckResetRequest(PlcReader plc, DatabaseHelper db) {
            try {
                if (db.CheckAndClearResetRequested()) {
                    plc.WriteResetZlecen(true);
                    Console.WriteLine($"[INFO] Przycisk 'Rozpocznij nowe zajecia' -> wyslano ResetZlecen=TRUE do PLC ({PlcReader.AdresResetZlecen})");
                }
            } catch (Exception ex) {
                Console.WriteLine($"[WARN] Nie udalo sie wyslac ResetZlecen do PLC: {ex.Message}");
            }
        }

        static void RunWithPlc(DatabaseHelper db)
        {
            using var plc = new PlcReader(PlcIpAddress);
            try
            {
                                plc.Connect();
                DateTime lastTrigger = DateTime.Now;
                
                plc.WeryfikujUkladPamieci();

                // 1. Sync zlecenia przy starcie
                SyncActiveOrderToPlc(plc, db);
                CheckResetRequest(plc, db);
                CheckAborts(plc, db);
                SyncWynikiQC(plc, db);
                SyncLicznikProdukcji(plc, db);
                SprzatnijZakonczoneZlecenia(plc, db);
                SyncStanowiskaIKontenery(plc, db);

                Console.WriteLine("\n[INFO] Oczekiwanie na sygnal z PLC (DB1.DBX0.0 = ZapisDoBazy)...\n");

                                DateTime lastSync = DateTime.Now;
                                DateTime lastStany = DateTime.Now;

                while (true)
                {
                    if (!plc.ReadTrigger())
                    {
                        Thread.Sleep(POLL_INTERVAL_MS);

                        // Szybki cykl (300 ms): jedna migawka DB_Data (stany + kontenery)
                        // oraz wysylka zlecenia. To na te rzeczy czeka operator, a kosztuja
                        // tylko dwa odczyty z PLC.
                        if ((DateTime.Now - lastStany).TotalMilliseconds >= 200)
                        {
                            SyncStanowiskaIKontenery(plc, db);
                            SyncActiveOrderToPlc(plc, db);
                            CheckResetRequest(plc, db);
                            lastStany = DateTime.Now;
                        }

                        // Wolny cykl (3 s): skan calej tablicy zlecen (28 kB) - kosztowny,
                        // wiec nie ma sensu robic go czesciej.
                        if ((DateTime.Now - lastSync).TotalSeconds >= 3)
                        {
                            CheckAborts(plc, db);
                            SyncWynikiQC(plc, db);
                            SyncLicznikProdukcji(plc, db);
                            // Zaraz po SyncWynikiQC - to ono zamyka zlecenia, wiec
                            // sprzatanie ma tu swiezy komplet zamknietych rekordow.
                            SprzatnijZakonczoneZlecenia(plc, db);
                            lastSync = DateTime.Now;
                        }
                        continue;
                    }

                    DateTime now  = DateTime.Now;
                    int splywMs   = (int)(now - lastTrigger).TotalMilliseconds;
                    Console.WriteLine($"\n> [{now:HH:mm:ss}] TRIGGER - pobieranie danych z PLC...");

                    var raw      = plc.ReadProductionData();
                    int postoiMs = Math.Max(0, splywMs - raw.CzasCykluMs);

                    // Lookup po nazwie - PLC wysyla stringi. Operator: stalt rekord systemowy
                    // (system nie sledzi juz pojedynczych pracownikow/logowan).
                    var stan = db.GetStanowiskoByName(raw.NazwaStanowiska);
                    var op   = db.GetSystemOperator();
                    var zl   = db.GetZlecenieByName(raw.NumerZlecenia);

                    // Ilosc: jesli PLC nie wyslal (= 0) to przyjmij 1 szt per trigger
                    int iloscWyprod = raw.IloscWyprodukowanych > 0 ? raw.IloscWyprodukowanych : 1;
                    // WynikQC z PLC lub derive z liczby wadliwych
                    bool wynikQC = raw.WynikQC && raw.LiczbaWadliwych == 0;

                    var data = BuildPlcData(zl, stan, op,
                                           raw.CzasCykluMs, zl.CzasPlanowanyMs,
                                           iloscWyprod, raw.LiczbaWadliwych,
                                           string.IsNullOrWhiteSpace(raw.KodPostoju) ? null : raw.KodPostoju.Trim(),
                                           wynikQC, lastTrigger, now, splywMs, postoiMs);

                    PrintCycleSummary(data, stan, op, zl);
                    SaveToDatabase(db, data, stan, op);
                    db.IncrementRozpoczeteSztuki(zl.ID);

                    // Stanowisko QC (ID=4): dolicz sztuke OK/NOK do zlecenia (limit = Ilosc_Sztuk,
                    // bez powtarzania az kazda bedzie OK).
                    if (stan.ID == 4)
                    {
                        db.IncrementQcWynik(zl.ID, wynikQC);
                        Console.WriteLine($"[INFO] QC: zlecenie {zl.ID} -> {(wynikQC ? "OK" : "NOK")}");
                    }

                    plc.ResetTrigger();
                    Console.WriteLine("> Trigger PLC zresetowany");
                    
                    // 2. Sync zlecenia na wypadek zmian w SQL
                    SyncActiveOrderToPlc(plc, db);
                    
                    Console.WriteLine("\n[INFO] Oczekiwanie na kolejny sygnal...");
                    lastTrigger = now;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[BLAD] {ex.Message}");
                Console.ResetColor();
                Console.WriteLine("\nNacisnij dowolny klawisz...");
                Console.ReadKey();
            }
        }

        // ================================================================
        //  HELPERS
        // ================================================================

        static PlcData BuildPlcData(ZlecenieData zl, StanowiskoData stan, OperatorData op,
                                    int czasCykluMs, int czasPlanowyMs,
                                    int iloscWyprod, int liczbaWad, string? kodPostoju,
                                    bool wynikQC, DateTime rozp, DateTime zak,
                                    int splywMs, int postoiMs)
        {
            return new PlcData
            {
                IDZlecenia           = zl.ID,
                IDStanowiska         = stan.ID,
                IDOperatora          = op.ID,
                CzasCykluMs          = Math.Max(1, czasCykluMs),
                CzasPlanowanyMs      = czasPlanowyMs > 0 ? czasPlanowyMs : zl.CzasPlanowanyMs,
                IloscWyprodukowanych = Math.Max(1, iloscWyprod),
                LiczbaWadliwych      = liczbaWad,
                KodPostoju           = kodPostoju,
                WynikQC              = wynikQC,
                CzasRozpoczecia      = rozp,
                CzasZakonczenia      = zak,
                CzasSplywuMs         = splywMs,
                CzasPostojuMs        = postoiMs
            };
        }

        static void SaveToDatabase(DatabaseHelper db, PlcData data,
                                   StanowiskoData stan, OperatorData op)
        {
            int realizacjaId = db.InsertRealizacja(data);
            Console.WriteLine($"│  Realizacja_Produkcji  [ID={realizacjaId}]");
            db.InsertKoszty(data, realizacjaId, op, stan);
            Console.WriteLine("│  Koszty");
            db.InsertWskazniki(data, realizacjaId);
            Console.WriteLine("│  Wskazniki (OEE/FTY)");
            Console.WriteLine("└─ Zapisano\n");
        }

        static void PrintHeader()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔══════════════════════════════════════════════════╗");
            Console.WriteLine("║      PlcToDb Middleware  v2.0                    ║");
            Console.WriteLine("║      Linia Montazowa — OEE / FTY Logger          ║");
            Console.WriteLine("╚══════════════════════════════════════════════════╝");
            Console.ResetColor();
        }

        static void PrintCycleSummary(PlcData d, StanowiskoData stan,
                                      OperatorData op, ZlecenieData zl)
        {
            double A = d.CzasSplywuMs > 0
                ? Math.Clamp((double)(d.CzasSplywuMs - d.CzasPostojuMs) / d.CzasSplywuMs, 0, 1)
                : 1.0;
            double P = (d.CzasPlanowanyMs > 0 && d.CzasCykluMs > 0)
                ? Math.Clamp((double)d.CzasPlanowanyMs / d.CzasCykluMs, 0, 1.5)
                : 1.0;
            double Q = d.IloscWyprodukowanych > 0
                ? Math.Clamp((double)(d.IloscWyprodukowanych - d.LiczbaWadliwych)
                             / d.IloscWyprodukowanych, 0, 1)
                : 1.0;
            double oee = A * P * Q;

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"│  Zlecenie  : {zl.NazwaZlecenia}");
            Console.WriteLine($"│  Stanowisko: {stan.Nazwa}");
            Console.WriteLine($"│  Operator  : {op.ImieNazwisko}");
            Console.ResetColor();
            Console.WriteLine($"│  Czas cyklu : {d.CzasCykluMs,7} ms  |  " +
                              $"Czas splywu: {d.CzasSplywuMs,7} ms  |  " +
                              $"Postoj: {d.CzasPostojuMs,7} ms");
            Console.WriteLine($"│  Wyprod.: {d.IloscWyprodukowanych}  Wadliwe: {d.LiczbaWadliwych}" +
                              (d.KodPostoju != null ? $"  Kod: {d.KodPostoju}" : ""));

            ConsoleColor oeeColor = oee >= 0.85 ? ConsoleColor.Green
                                  : oee >= 0.60 ? ConsoleColor.Yellow
                                  : ConsoleColor.Red;
            Console.ForegroundColor = oeeColor;
            Console.WriteLine($"│  OEE: {oee:P1}  (A={A:P1}  P={P:P1}  Q={Q:P1})");
            Console.ResetColor();
        }
    }
}












