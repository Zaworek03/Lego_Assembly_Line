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
                        static int _lastSyncedOrderId = -1;
        static int _lastSyncedPartNo = -1;
        static int _lastSyncedCount = 0;

        static void SyncActiveOrderToPlc(PlcReader plc, DatabaseHelper db) {
            try {
                var activeOrders = db.GetActiveOrders();
                int highestId = activeOrders.Count > 0 ? activeOrders[0].id : -1;
                // PartNo = potrzebna ilosc sztuk wprost ze zlecenia (Ilosc_Sztuk).
                // Zliczanie/inkrementacje kolejnych sztuk przejela logika w TIA.
                int highestPart = activeOrders.Count > 0 ? activeOrders[0].iloscSztuk : -1;

                if (highestId != _lastSyncedOrderId || highestPart != _lastSyncedPartNo || activeOrders.Count != _lastSyncedCount)
                {
                    Console.WriteLine($"[INFO] SQL -> PLC: Synchronizacja {activeOrders.Count} aktywnych zlecen (DB3)...");
                    if (activeOrders.Count > 0)
                    {
                        plc.WriteOrderToPlc(
                            activeOrders[0].id,
                            activeOrders[0].idWyrobu + 1,
                            activeOrders[0].iloscSztuk,
                            activeOrders[0].priority
                        );
                        Console.WriteLine($"[INFO] Przeslano zlecenie do PLC: ZlecenieID={activeOrders[0].id} PartNo(Ilosc)={activeOrders[0].iloscSztuk}");
                    }

                    _lastSyncedOrderId = highestId;
                    _lastSyncedPartNo = highestPart;
                    _lastSyncedCount = activeOrders.Count;
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
        static void CheckOrderStarted(PlcReader plc, DatabaseHelper db) {
            try {
                var (state, orderNo) = plc.ReadStanowiskoProdukcja(1);
                if (state == 1 && orderNo > 0) {
                    db.MarkOrderStartedIfNew(orderNo);
                }
            } catch (Exception ex) {
                Console.WriteLine($"[WARN] Nie udalo sie sprawdzic startu zlecenia: {ex.Message}");
            }
        }

        static void CheckResetRequest(PlcReader plc, DatabaseHelper db) {
            try {
                if (db.CheckAndClearResetRequested()) {
                    plc.WriteResetZlecen(true);
                    Console.WriteLine("[INFO] Przycisk 'Rozpocznij nowe zajecia' -> wyslano ResetZlecen=TRUE do PLC (DB3.DBX27876.0)");
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
                
                // 1. Sync zlecenia przy starcie
                SyncActiveOrderToPlc(plc, db);
                CheckResetRequest(plc, db);
                CheckAborts(plc, db);
                CheckOrderStarted(plc, db);

                Console.WriteLine("\n[INFO] Oczekiwanie na sygnal z PLC (DB1.DBX0.0 = ZapisDoBazy)...\n");

                                DateTime lastSync = DateTime.Now;

                while (true)
                {
                    if (!plc.ReadTrigger())
                    {
                        Thread.Sleep(POLL_INTERVAL_MS);
                        if ((DateTime.Now - lastSync).TotalSeconds >= 3)
                        {
                            SyncActiveOrderToPlc(plc, db);
                            CheckResetRequest(plc, db);
                            CheckAborts(plc, db);
                            CheckOrderStarted(plc, db);
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












