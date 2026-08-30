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
        static void SyncActiveOrderToPlc(PlcReader plc, DatabaseHelper db, byte[]? bufor = null) {
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

                // Bez gotowego bufora ta funkcja musialaby sama zeskanowac 28 kB (ok. 4 s)
                // i zablokowac watek, na ktorym siedzi. Wolamy ja wiec ze skanera, ktory
                // ten bufor juz ma.
                if (bufor == null) return;

                var naLinii = plc.LiczSztukiWszystkichZlecen(bufor);
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

        static readonly int[] _ostatniStanStanowiska      = { -1, -1, -1, -1 };
        static readonly int[] _ostatnieZlecenieStanowiska = { -1, -1, -1, -1 };

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
            //
            // ReadPrzeplywSztuki wybiera rekord TEJ sztuki (najpozniejszy EndTime na tym
            // stanowisku), a nie pierwszy slot z danym ID - zlecenie wielosztukowe ma
            // kilka slotow o tym samym ID i sztuki moga jechac rownolegle.
            string zrodlo = "stoper";
            var przeplyw = plc.ReadPrzeplywSztuki(zlecenie, nr);
            if (przeplyw is { ActualSek: > 0 }) {
                int zPlcMs = przeplyw.ActualSek * 1000;
                double stosunek = (double)zPlcMs / czasMs;
                if (stosunek is > 0.3 and < 3.0) {
                    czasMs = zPlcMs;
                    zrodlo = "PLC";
                } else {
                    Console.WriteLine($"[WARN] Stanowisko {nr}: ActualTime z PLC = {przeplyw.ActualSek} s "
                                    + $"vs stoper {czasMs / 1000.0:0.0} s - rozbieznosc x{stosunek:0.00}, "
                                    + "biore stoper (sprawdz jednostke ActualTime w TIA).");
                }
            }

            try {
                int? wydajnosc = db.ZapiszCyklStanowiska(nr, zlecenie, czasMs, przeplyw);
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
                    int stan     = m.Stany[nr - 1];
                    int zlecenie = m.NumeryZlecen[nr - 1];

                    // Zapisujemy takze przy samej zmianie numeru zlecenia - stanowisko
                    // moze dostac kolejna sztuke bez zmiany stanu.
                    if (stan == _ostatniStanStanowiska[nr - 1] &&
                        zlecenie == _ostatnieZlecenieStanowiska[nr - 1]) continue;

                    int poprzedni = _ostatniStanStanowiska[nr - 1];
                    db.ZapiszStanStanowiska(nr, stan, zlecenie);
                    if (stan != poprzedni)
                        Console.WriteLine($"[INFO] Stanowisko {nr}: stan {poprzedni} -> {stan} (zlecenie {zlecenie})");

                    _ostatniStanStanowiska[nr - 1]      = stan;
                    _ostatnieZlecenieStanowiska[nr - 1] = zlecenie;

                    if (stan != poprzedni)
                        ObsluzPomiarCyklu(plc, db, nr, poprzedni, stan, zlecenie);
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
                foreach (var (id, status) in db.GetZleceniaDoWyczyszczeniaWPlc()) {
                    try {
                        int slot = plc.ClearOrderSlot(id);
                        plc.ClearNastepneZlecenieIf(id);
                        // Zakonczone = klocki faktycznie poszly w wyrob, wiec schodza ze stanu.
                        // Anulowane = rezerwacja wraca do puli.
                        db.RozliczMaterialyZlecenia(id, status == "Zakonczone");
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

        /// <summary>
        /// Osobny watek do CIEZKICH odczytow tablicy Zlecenie[] (QC, aborty, sprzatanie).
        ///
        /// Powod: jeden skan tej tablicy to 28 kB, czyli ok. 126 telegramow S7 - zmierzone
        /// ~4 s na tym laczu. Wczesniej te skany siedzialy w glownej petli, wiec odczyt
        /// stanu stanowisk zamiast co 200 ms wypadal raz na 8,4 s i operator czekal
        /// kilka sekund, zanim strona zauwazyla, ze stanowisko ruszylo.
        ///
        /// Watek ma WLASNE polaczenie z PLC - S7.Net nie jest bezpieczny watkowo, wiec
        /// wspoldzielenie jednego obiektu Plc konczyloby sie przeplotem telegramow.
        /// </summary>
        static void SkanerTablicy(string ip, DatabaseHelper db)
        {
            using var plc = new PlcReader(ip);
            try { plc.Connect(); }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Skaner tablicy: brak polaczenia z PLC ({ex.Message}) - QC i aborty nieaktywne.");
                return;
            }
            Console.WriteLine("[INFO] Skaner tablicy Zlecenie[] wystartowal (osobne polaczenie).");

            var lastLicznik = DateTime.MinValue;
            var lastPelny   = DateTime.MinValue;
            bool pelnySkanNaStale = false;

            while (true)
            {
                try
                {
                    var zegar = System.Diagnostics.Stopwatch.StartNew();

                    // JEDEN odczyt na cykl - karmi i QC, i aborty. Wczesniej kazda z tych
                    // funkcji czytala te same 28 kB osobno, czyli 8 s zamiast 4 s.
                    //
                    // Domyslnie czytamy tylko poczatek tablicy (32 sloty, ~0,6 s zamiast ~4 s) -
                    // to na tym odczycie wisi czas reakcji na zakonczenie zlecenia. Co pewien
                    // czas robimy pelny skan, zeby nie przegapic niczego dalej w tablicy;
                    // gdyby okno okazalo sie za male, przechodzimy na pelne na stale.
                    bool pelny = pelnySkanNaStale || (DateTime.Now - lastPelny).TotalSeconds >= 30;
                    var bufor = plc.ReadTablicaZlecen(pelny ? 200 : PlcReader.SLOTY_SKROCONE);
                    if (pelny) lastPelny = DateTime.Now;

                    if (bufor != null && !pelnySkanNaStale && PlcReader.OknoZaMale(bufor))
                    {
                        pelnySkanNaStale = true;
                        Console.WriteLine("[WARN] Zlecenia siegaja poza 32. slot - przechodze na pelny skan tablicy "
                                        + "(wolniejsza reakcja). Sprawdz, czy sloty zamknietych zlecen sa zerowane.");
                    }
                    if (bufor != null)
                    {
                        foreach (var s in plc.ReadSztukiPoQC(bufor))
                        {
                            try { db.ZarejestrujSztukePoQC(s.IdZlecenia, s.PartNo, s.WynikOK); }
                            catch (Exception ex) { Console.WriteLine($"[WARN] QC zlecenie {s.IdZlecenia}/{s.PartNo}: {ex.Message}"); }
                        }

                        foreach (var (slot, idZlecenia, stanowiskoNr) in plc.ReadAbortEvents(bufor))
                        {
                            try { db.ZapiszPowiadomienieAbortu(slot, idZlecenia, stanowiskoNr); }
                            catch (Exception ex) { Console.WriteLine($"[WARN] Abort zlecenie {idZlecenia}: {ex.Message}"); }
                        }

                        SprzatnijZakonczoneZlecenia(plc, db);

                        // Podanie kolejnego zlecenia tez potrzebuje tego bufora (liczy,
                        // ile sztuk danego zlecenia jest juz na linii).
                        SyncActiveOrderToPlc(plc, db, bufor);
                    }

                    // Licznik ogolny (DoneAllTime) przeniesiony do glownej petli - to jeden
                    // maly odczyt, a zasila kafelek "Wyprodukowano ogolnie", wiec nie ma
                    // powodu, zeby czekal na skan tablicy.

                    // Skan trwa tyle, ile trwa; dokladamy tylko krotka przerwe, zeby
                    // nie zajezdzic lacza w 100%.
                    int przerwa = Math.Max(200, 1000 - (int)zegar.ElapsedMilliseconds);
                    Thread.Sleep(przerwa);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN] Skaner tablicy: {ex.Message}");
                    Thread.Sleep(2000);
                }
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

                // Ciezkie skany tablicy Zlecenie[] ida na wlasny watek i wlasne polaczenie,
                // zeby nie opozniac odczytu stanu stanowisk w glownej petli.
                new Thread(() => SkanerTablicy(PlcIpAddress, db))
                { IsBackground = true, Name = "SkanerTablicy" }.Start();

                Console.WriteLine("\n[INFO] Oczekiwanie na sygnal z PLC (DB1.DBX0.0 = ZapisDoBazy)...\n");

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
                            // Odstep MIEDZY kolejnymi odczytami stanu - to on decyduje,
                            // po jakim czasie strona dowie sie, ze stanowisko ruszylo.
                            double odstep = (DateTime.Now - lastStany).TotalMilliseconds;
                            var zegar = System.Diagnostics.Stopwatch.StartNew();

                            SyncStanowiskaIKontenery(plc, db);
                            long tMigawka = zegar.ElapsedMilliseconds;
                            // SyncActiveOrderToPlc przeniesione do SkanerTablicy - potrzebuje bufora 28 kB.
                            // Licznik produkcji zostaje tu: jedno slowo z PLC, a od niego zalezy
                            // kafelek "Wyprodukowano ogolnie" i wykres.
                            SyncLicznikProdukcji(plc, db);
                            long tZlecenie = zegar.ElapsedMilliseconds - tMigawka;
                            CheckResetRequest(plc, db);
                            lastStany = DateTime.Now;

                            if (odstep > 450 || zegar.ElapsedMilliseconds > 300)
                                Console.WriteLine($"[PROFIL] stany: odstep={odstep:0} ms, "
                                                + $"migawka={tMigawka} ms, zlecenie={tZlecenie} ms, "
                                                + $"razem={zegar.ElapsedMilliseconds} ms");
                        }

                        // Sredni cykl (1 s): wyniki QC. To one przestawiaja zlecenie na
                        // 'Zakonczone', wiec przy 3 s status na stronie potrafil pojawic sie
                        // dopiero po 3 s (Middleware) + 2 s (pulpit). Skan tablicy Zlecenie[]
                        // to 28 kB / ok. 55 ms, wiec przy 1 s to nadal ~5% obciazenia lacza.
                        // QC, aborty i licznik przeniesione na osobny watek (SkanerTablicy).
                        // Skan tablicy Zlecenie[] kosztuje ~4 s i blokowal tu odczyt stanowisk.
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
                    // podanie zlecenia obsluguje SkanerTablicy (ma gotowy bufor)
                    
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












