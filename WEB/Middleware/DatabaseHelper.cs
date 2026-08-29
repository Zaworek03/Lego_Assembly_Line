using Microsoft.Data.SqlClient;

namespace PlcToDbMiddleware
{
    /// <summary>
    ///  Operacje na bazie SQL Server: lookup po nazwach (zgodnie z danymi z PLC)
    ///  oraz zapis do tabel transakcyjnych.
    /// </summary>
    public class DatabaseHelper
    {
        private readonly string _connectionString;

        public DatabaseHelper(string connectionString)
        {
            _connectionString = connectionString;
        }

        // ============================================================
        // LOOKUP — wyszukiwanie po nazwie (PLC wysyla stringi)
        // ============================================================

        /// <summary>
        ///  Szuka stanowiska po nazwie (case-insensitive).
        ///  Rzuca wyjatek gdy nie znajdzie — nalezy wpisac nazwy do tabeli Stanowisko.
        /// </summary>
        public StanowiskoData GetStanowiskoByName(string nazwa)
        {
            // COLLATE ..._CI_AI = ignoruje wielkosc liter I znaki diakrytyczne, wiec PLC moze
            // wysylac "Stanowisko Montaz 1" i dopasuje sie do "Stanowisko Montaż 1" w bazie.
            const string sql = @"
                SELECT ID_Stanowiska, Nazwa_Stanowiska,
                       ISNULL(Stawka_Amortyzacyjna, 0)
                FROM   [dbo].[Stanowisko]
                WHERE  LTRIM(RTRIM(Nazwa_Stanowiska)) COLLATE Latin1_General_CI_AI
                     = LTRIM(RTRIM(@Nazwa))           COLLATE Latin1_General_CI_AI";

            using var conn = OpenConnection();
            using var cmd  = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Nazwa", nazwa);
            using var rdr = cmd.ExecuteReader();

            if (!rdr.Read())
                throw new Exception(
                    $"[DB] Stanowisko '{nazwa}' nie istnieje w tabeli Stanowisko! " +
                    $"Sprawdz czy nazwa w TIA Portal (pole Stanowisko) zgadza sie z baza.");

            return new StanowiskoData
            {
                ID                  = Convert.ToInt32(rdr[0]),
                Nazwa               = rdr.GetString(1),
                StawkaAmortyzacyjna = rdr.GetDecimal(2)
            };
        }

        /// <summary>
        /// Zwraca staly "systemowy" rekord operatora (ID=1) - system nie sledzi juz
        /// pojedynczych pracownikow/logowan, ale Realizacja_Produkcji/Harmonogram
        /// wymagaja (NOT NULL) jakiegos ID_Operatora do zapisu.
        /// </summary>
        public OperatorData GetSystemOperator()
        {
            const string sql = @"
                SELECT ID_Operatora, Imie_Nazwisko, ISNULL(Stawka_Godzinowa, 0)
                FROM   [dbo].[Operator]
                WHERE  ID_Operatora = 1";

            using var conn = OpenConnection();
            using var cmd  = new SqlCommand(sql, conn);
            using var rdr = cmd.ExecuteReader();

            if (!rdr.Read())
                throw new Exception("[DB] Brak systemowego rekordu Operator (ID=1) w tabeli Operator!");

            return new OperatorData
            {
                ID              = Convert.ToInt32(rdr[0]),
                ImieNazwisko    = rdr.GetString(1),
                StawkaGodzinowa = rdr.GetDecimal(2)
            };
        }

        /// <summary>Szuka zlecenia po nazwie/numerze zlecenia (case-insensitive).</summary>
        public ZlecenieData GetZlecenieByName(string nazwaZlecenia)
        {
            const string sql = @"
                SELECT ID_Zlecenia, Nazwa_Zlecenia, Ilosc_Sztuk,
                       ISNULL(Czas_Planowany_ms, 0)
                FROM   [dbo].[Zlecenie_Produkcyjne]
                WHERE  LOWER(LTRIM(RTRIM(Nazwa_Zlecenia))) = LOWER(LTRIM(RTRIM(@Nazwa)))";

            using var conn = OpenConnection();
            using var cmd  = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Nazwa", nazwaZlecenia);
            using var rdr = cmd.ExecuteReader();

            if (!rdr.Read())
                throw new Exception(
                    $"[DB] Zlecenie '{nazwaZlecenia}' nie istnieje w tabeli Zlecenie_Produkcyjne! " +
                    $"Dodaj zlecenie lub popraw wartosc pola Numer_Zlecenia w TIA Portal.");

            return new ZlecenieData
            {
                ID              = Convert.ToInt32(rdr[0]),
                NazwaZlecenia   = rdr.GetString(1),
                IloscSztuk      = Convert.ToInt32(rdr[2]),
                CzasPlanowanyMs = Convert.ToInt32(rdr[3])
            };
        }

        // ============================================================
        // INSERT — Realizacja_Produkcji
        // ============================================================

        public int InsertRealizacja(PlcData d)
        {
            const string sql = @"
                INSERT INTO [dbo].[Realizacja_Produkcji]
                    (ID_Zlecenia, ID_Stanowiska, ID_Operatora,
                     Czas_Rozpoczecia, Czas_Zakonczenia,
                     Czas_Splywu_ms, Czas_Cyklu_ms, Czas_Postoju_ms,
                     Kod_Postoju, Ilosc_Wyprodukowanych, Liczba_Wadliwych, Wynik_QC)
                VALUES
                      (@Zlecenie, @Stanowisko, @Operator,
                       @Rozp, @Zak,
                       @Splyw, @Cykl, @Postoj,
                       @KodPostoju, @Wyprod, @Wadliwe, @QC);
                  SELECT SCOPE_IDENTITY();";

            using var conn = OpenConnection();
            using var cmd  = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Zlecenie",   d.IDZlecenia);
            cmd.Parameters.AddWithValue("@Stanowisko", d.IDStanowiska);
            cmd.Parameters.AddWithValue("@Operator",   d.IDOperatora);
            cmd.Parameters.AddWithValue("@Rozp",       d.CzasRozpoczecia);
            cmd.Parameters.AddWithValue("@Zak",        d.CzasZakonczenia);
            cmd.Parameters.AddWithValue("@Splyw",      d.CzasSplywuMs);
            cmd.Parameters.AddWithValue("@Cykl",       d.CzasCykluMs);
            cmd.Parameters.AddWithValue("@Postoj",     d.CzasPostojuMs);
            cmd.Parameters.AddWithValue("@KodPostoju", string.IsNullOrWhiteSpace(d.KodPostoju)
                                                           ? (object)DBNull.Value
                                                           : d.KodPostoju);
            cmd.Parameters.AddWithValue("@Wyprod",     d.IloscWyprodukowanych);
            cmd.Parameters.AddWithValue("@Wadliwe",    d.LiczbaWadliwych);
            cmd.Parameters.AddWithValue("@QC",         d.WynikQC ? 1 : 0);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        // ============================================================
        // INSERT — Koszty (obliczane per cykl)
        // ============================================================

        public void InsertKoszty(PlcData d, int realizacjaId,
                                 OperatorData op, StanowiskoData stan)
        {
            double h = d.CzasCykluMs / 3_600_000.0;

            decimal kosztOp   = (decimal)(h * (double)op.StawkaGodzinowa);
            decimal kosztStan = (decimal)(h * (double)stan.StawkaAmortyzacyjna);
            decimal kosztMat  = 0m;
            decimal kosztCal  = kosztOp + kosztStan + kosztMat;

            const string sql = @"
                INSERT INTO [dbo].[Koszty]
                    (ID_Zlecenia, ID_Realizacji,
                     Koszt_Materialow, Koszt_Operatorow, Koszt_Pracy_Stanowisk, Koszt_Calkowity)
                VALUES
                    (@Zlecenie, @Realizacja,
                     @KosztMat, @KosztOp, @KosztStan, @KosztCal)";

            using var conn = OpenConnection();
            using var cmd  = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Zlecenie",   d.IDZlecenia);
            cmd.Parameters.AddWithValue("@Realizacja",  realizacjaId);
            cmd.Parameters.AddWithValue("@KosztMat",   kosztMat);
            cmd.Parameters.AddWithValue("@KosztOp",    kosztOp);
            cmd.Parameters.AddWithValue("@KosztStan",  kosztStan);
            cmd.Parameters.AddWithValue("@KosztCal",   kosztCal);
            cmd.ExecuteNonQuery();
        }

        // ============================================================
        // INSERT — Wskazniki OEE / FTY per cykl
        // ============================================================

                public List<(int id, int idWyrobu, int iloscSztuk, int priority, int rozpoczetoSztuk)> GetActiveOrders()
        {
            const string sql = @"
                SELECT TOP 500 ID_Zlecenia, ID_Wyrobu, Ilosc_Sztuk, PriorytetNum, ISNULL(Rozpoczeto_Sztuk, 0)
                FROM [dbo].[Zlecenie_Produkcyjne]
                WHERE Status_Zlecenia IN ('W toku', 'Aktywne', 'Oczekujące', 'Nowe') 
                  AND IsDeleted = 0
                  AND ISNULL(Rozpoczeto_Sztuk, 0) < Ilosc_Sztuk
                ORDER BY PriorytetNum DESC, DueTime ASC";

            using var conn = OpenConnection();
            using var cmd = new SqlCommand(sql, conn);
            using var rdr = cmd.ExecuteReader();

            var list = new List<(int, int, int, int, int)>();
            while (rdr.Read())
            {
                list.Add((
                    Convert.ToInt32(rdr[0]),  // id
                    Convert.ToInt32(rdr[1]),  // idWyrobu
                    Convert.ToInt32(rdr[2]),  // iloscSztuk
                    Convert.ToInt32(rdr[3]),  // priority
                    Convert.ToInt32(rdr[4])   // rozpoczetoSztuk
                ));
            }
            return list;
        }

        public void IncrementRozpoczeteSztuki(int idZlecenia)
        {
            const string sql = @"
                UPDATE [dbo].[Zlecenie_Produkcyjne]
                SET Rozpoczeto_Sztuk = ISNULL(Rozpoczeto_Sztuk, 0) + 1,
                    Status_Zlecenia = 'W toku'
                WHERE ID_Zlecenia = @ID;
                ";
            using var conn = OpenConnection();
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ID", idZlecenia);
            cmd.ExecuteNonQuery();
        }

        public void InsertWskazniki(PlcData d, int realizacjaId)
        {
            double dostepnosc = d.CzasSplywuMs > 0
                ? Math.Clamp((double)(d.CzasSplywuMs - d.CzasPostojuMs) / d.CzasSplywuMs, 0.0, 1.0)
                : 1.0;

            double wydajnosc = (d.CzasPlanowanyMs > 0 && d.CzasCykluMs > 0)
                ? Math.Clamp((double)d.CzasPlanowanyMs / d.CzasCykluMs, 0.0, 1.5)
                : 1.0;

            double jakosc = d.IloscWyprodukowanych > 0
                ? Math.Clamp((double)(d.IloscWyprodukowanych - d.LiczbaWadliwych)
                             / d.IloscWyprodukowanych, 0.0, 1.0)
                : 1.0;

            double oee     = dostepnosc * wydajnosc * jakosc;
            double fty     = jakosc;
            double h       = d.CzasCykluMs / 3_600_000.0;
            double wydPracy = h > 0 ? Math.Min(d.IloscWyprodukowanych / h, 999999.0) : 0.0;

            const string sql = @"
                INSERT INTO [dbo].[Wskazniki]
                    (ID_Zlecenia, ID_Realizacji, ID_Stanowiska,
                     Wydajnosc, Dostepnosc, Jakosc, Wskaznik_OEE,
                     Czas_Realizacji_ms, Wydajnosc_Pracy_Operatora, Czas_Cyklu_ms, Wskaznik_FTY)
                VALUES
                    (@Zlecenie, @Realizacja, @Stanowisko,
                     @P, @A, @Q, @OEE,
                     @CzasReal, @WydPracy, @CzasCyklu, @FTY)";

            using var conn = OpenConnection();
            using var cmd  = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Zlecenie",   d.IDZlecenia);
            cmd.Parameters.AddWithValue("@Realizacja",  realizacjaId);
            cmd.Parameters.AddWithValue("@Stanowisko",  d.IDStanowiska);
            cmd.Parameters.AddWithValue("@P",           (decimal)wydajnosc);
            cmd.Parameters.AddWithValue("@A",           (decimal)dostepnosc);
            cmd.Parameters.AddWithValue("@Q",           (decimal)jakosc);
            cmd.Parameters.AddWithValue("@OEE",         (decimal)oee);
            cmd.Parameters.AddWithValue("@CzasReal",    d.CzasSplywuMs);
            cmd.Parameters.AddWithValue("@WydPracy",    (decimal)wydPracy);
            cmd.Parameters.AddWithValue("@CzasCyklu",   d.CzasCykluMs);
            cmd.Parameters.AddWithValue("@FTY",         (decimal)fty);
            cmd.ExecuteNonQuery();
        }

        // ============================================================
        // POWIADOMIENIA — Abort dotyczy TYLKO jednej sztuki, nie calego zlecenia,
        // wiec zlecenie NIE jest automatycznie anulowane - tylko zgloszenie w UI.
        // ============================================================

        /// <summary>
        /// Zapisuje powiadomienie o porzuceniu (Abort) pojedynczej sztuki na danym stanowisku.
        /// Deduplikacja trwala w bazie (PowiadomieniaAbortProcessed) po (slotIndex, stanowisko) -
        /// przezywa restart Middleware, w przeciwienstwie do sledzenia tylko w pamieci.
        /// </summary>
        public void ZapiszPowiadomienieAbortu(int slotIndex, int idZlecenia, int idStanowiska)
        {
            using var conn = OpenConnection();

            // Sprawdz czy to zdarzenie juz zostalo przetworzone (dowolna wczesniejsza sesja Middleware)
            using (var check = new SqlCommand(
                "SELECT 1 FROM PowiadomieniaAbortProcessed WHERE SlotIndex=@Slot AND ID_Stanowiska=@IDSt", conn))
            {
                check.Parameters.AddWithValue("@Slot", slotIndex);
                check.Parameters.AddWithValue("@IDSt", idStanowiska);
                if (check.ExecuteScalar() != null) return; // juz zgloszone
            }
            using (var mark = new SqlCommand(
                "INSERT INTO PowiadomieniaAbortProcessed (SlotIndex, ID_Stanowiska) VALUES (@Slot, @IDSt)", conn))
            {
                mark.Parameters.AddWithValue("@Slot", slotIndex);
                mark.Parameters.AddWithValue("@IDSt", idStanowiska);
                mark.ExecuteNonQuery();
            }

            string nazwaZlecenia = "?", nazwaStanowiska = $"Stanowisko {idStanowiska}";
            using (var lookup = new SqlCommand(
                "SELECT Nazwa_Zlecenia FROM Zlecenie_Produkcyjne WHERE ID_Zlecenia=@ID", conn))
            {
                lookup.Parameters.AddWithValue("@ID", idZlecenia);
                var r = lookup.ExecuteScalar() as string;
                if (r != null) nazwaZlecenia = r;
            }
            using (var lookup = new SqlCommand(
                "SELECT Nazwa_Stanowiska FROM Stanowisko WHERE ID_Stanowiska=@ID", conn))
            {
                lookup.Parameters.AddWithValue("@ID", idStanowiska);
                var r = lookup.ExecuteScalar() as string;
                if (r != null) nazwaStanowiska = r;
            }

            var tresc = $"Porzucono sztukę na {nazwaStanowiska} (zlecenie {nazwaZlecenia}).";
            using var cmd = new SqlCommand(@"
                INSERT INTO Powiadomienia (Typ, Tresc, ID_Zlecenia, ID_Stanowiska)
                VALUES ('AbortStanowiska', @Tresc, @IDZ, @IDSt)", conn);
            cmd.Parameters.AddWithValue("@Tresc", tresc);
            cmd.Parameters.AddWithValue("@IDZ", idZlecenia);
            cmd.Parameters.AddWithValue("@IDSt", idStanowiska);
            cmd.ExecuteNonQuery();

            Console.WriteLine($"[INFO] Powiadomienie: {tresc}");
        }

        // ============================================================
        // START ZLECENIA (Stanowisko1.Production.State -> 'W toku')
        // ============================================================

        /// <summary>Jesli zlecenie jest jeszcze 'Nowe', przelacza je na 'W toku' (Start nacisniety na Stanowisku 1).</summary>
        public void MarkOrderStartedIfNew(int idZlecenia)
        {
            using var conn = OpenConnection();
            using var cmd = new SqlCommand(@"
                UPDATE Zlecenie_Produkcyjne
                SET Status_Zlecenia = 'W toku', StartedAt = GETDATE()
                WHERE ID_Zlecenia = @ID AND Status_Zlecenia = 'Nowe'", conn);
            cmd.Parameters.AddWithValue("@ID", idZlecenia);
            cmd.ExecuteNonQuery();
        }

        // ============================================================
        // WYNIK QC (per sztuka) — zlicza OK/NOK do limitu Ilosc_Sztuk,
        // BEZ mechaniki "powtarzaj az wszystkie OK".
        // ============================================================

        /// <summary>
        /// Rejestruje sztuke po QC. Deduplikacja trwala po (ID_Zlecenia, PartNo), wiec
        /// ta sama sztuka nie policzy sie dwa razy mimo cyklicznego skanowania tablicy PLC.
        /// Zwraca true, jesli sztuka byla nowa i zostala doliczona.
        /// </summary>
        public bool ZarejestrujSztukePoQC(int idZlecenia, int partNo, bool ok)
        {
            using var conn = OpenConnection();

            using (var check = new SqlCommand(
                "SELECT 1 FROM SztukiPrzetworzone WHERE ID_Zlecenia=@ID AND PartNo=@P", conn))
            {
                check.Parameters.AddWithValue("@ID", idZlecenia);
                check.Parameters.AddWithValue("@P", partNo);
                if (check.ExecuteScalar() != null) return false;   // juz policzona
            }

            using (var ins = new SqlCommand(
                "INSERT INTO SztukiPrzetworzone (ID_Zlecenia, PartNo, WynikOK) VALUES (@ID, @P, @OK)", conn))
            {
                ins.Parameters.AddWithValue("@ID", idZlecenia);
                ins.Parameters.AddWithValue("@P", partNo);
                ins.Parameters.AddWithValue("@OK", ok);
                ins.ExecuteNonQuery();
            }

            IncrementQcWynik(idZlecenia, ok);
            Console.WriteLine($"[INFO] QC: zlecenie {idZlecenia}, sztuka {partNo} -> {(ok ? "OK" : "NOK")}");
            return true;
        }

        /// <summary>
        /// Dolicza 1 sztuke OK lub NOK do zlecenia. Gdy SztukOK+SztukNOK osiagnie
        /// Ilosc_Sztuk, zlecenie konczy sie automatycznie (niezaleznie od wyniku QC).
        /// </summary>
        public void IncrementQcWynik(int idZlecenia, bool ok)
        {
            using var conn = OpenConnection();
            using var cmd = new SqlCommand(@"
                UPDATE Zlecenie_Produkcyjne
                SET SztukOK  = SztukOK  + CASE WHEN @OK = 1 THEN 1 ELSE 0 END,
                    SztukNOK = SztukNOK + CASE WHEN @OK = 1 THEN 0 ELSE 1 END
                WHERE ID_Zlecenia = @ID", conn);
            cmd.Parameters.AddWithValue("@OK", ok ? 1 : 0);
            cmd.Parameters.AddWithValue("@ID", idZlecenia);
            cmd.ExecuteNonQuery();

            using var cmdDone = new SqlCommand(@"
                UPDATE Zlecenie_Produkcyjne
                SET Status_Zlecenia = 'Zakonczone', CompletedAt = GETDATE()
                WHERE ID_Zlecenia = @ID
                  AND Status_Zlecenia <> 'Zakonczone'
                  AND (SztukOK + SztukNOK) >= Ilosc_Sztuk", conn);
            cmdDone.Parameters.AddWithValue("@ID", idZlecenia);
            cmdDone.ExecuteNonQuery();
        }

        // ============================================================
        // STAN STANOWISK (Production.State z DB_Data)
        // ============================================================

        /// <summary>
        /// Zapisuje faktyczny stan stanowiska prosto z PLC (0=bezczynne, 1=montaz,
        /// 2=konczenie, 3=awaria). Wczesniej aktywnosc byla zgadywana z czasu ostatniego
        /// zapisu do bazy, co przy dlugich cyklach dawalo falszywy obraz.
        /// </summary>
        public void ZapiszStanStanowiska(int idStanowiska, int stan)
        {
            using var conn = OpenConnection();
            using var cmd = new SqlCommand(@"
                UPDATE Stanowisko
                SET Stan_Produkcji = @Stan, Stan_Aktualizacja = GETDATE()
                WHERE ID_Stanowiska = @ID", conn);
            cmd.Parameters.AddWithValue("@Stan", stan);
            cmd.Parameters.AddWithValue("@ID", idStanowiska);
            cmd.ExecuteNonQuery();
        }

        // ============================================================
        // KONTENERY Z KLOCKAMI (HMI.Container[1..3] z DB_Data)
        // ============================================================

        /// <summary>Zapisuje liczbe pojemnikow na torze i zglasza powiadomienie, gdy tor sie oprozni.</summary>
        public void ZapiszKontener(int idStanowiska, int nrToru, int wartosc, int poprzednia)
        {
            using var conn = OpenConnection();
            using (var cmd = new SqlCommand(@"
                UPDATE Kontenery SET Wartosc = @W, Aktualizacja = GETDATE()
                WHERE ID_Stanowiska = @ID AND NrToru = @T", conn))
            {
                cmd.Parameters.AddWithValue("@W", wartosc);
                cmd.Parameters.AddWithValue("@ID", idStanowiska);
                cmd.Parameters.AddWithValue("@T", nrToru);
                cmd.ExecuteNonQuery();
            }

            // Powiadomienie tylko przy SPADKU do poziomu alarmowego - inaczej po kazdym
            // dolozeniu i zdjeciu pojemnika sypalyby sie duplikaty.
            if (wartosc > 1 || poprzednia <= wartosc || poprzednia < 0) return;

            string material = "?", stanowisko = $"Stanowisko {idStanowiska}";
            using (var lookup = new SqlCommand(@"
                SELECT m.Nazwa_Materialu, s.Nazwa_Stanowiska
                FROM Kontenery k
                JOIN Material m   ON k.ID_Materialu  = m.ID_Materialu
                JOIN Stanowisko s ON k.ID_Stanowiska = s.ID_Stanowiska
                WHERE k.ID_Stanowiska = @ID AND k.NrToru = @T", conn))
            {
                lookup.Parameters.AddWithValue("@ID", idStanowiska);
                lookup.Parameters.AddWithValue("@T", nrToru);
                using var rdr = lookup.ExecuteReader();
                if (rdr.Read()) { material = rdr.GetString(0); stanowisko = rdr.GetString(1); }
            }

            string tresc = wartosc == 0
                ? $"BRAK klocków: {material} — tor {nrToru}, {stanowisko}. Dołóż pojemnik!"
                : $"Kończą się klocki: {material} — tor {nrToru}, {stanowisko} (został 1 pojemnik).";

            using var ins = new SqlCommand(@"
                INSERT INTO Powiadomienia (Typ, Tresc, ID_Stanowiska)
                VALUES ('BrakKlockow', @Tresc, @IDSt)", conn);
            ins.Parameters.AddWithValue("@Tresc", tresc);
            ins.Parameters.AddWithValue("@IDSt", idStanowiska);
            ins.ExecuteNonQuery();

            Console.WriteLine($"[INFO] {tresc}");
        }

        // ============================================================
        // LICZNIK PRODUKCJI (DoneAllTime z PLC)
        // ============================================================

        /// <summary>
        /// Zapisuje laczna produkcje odczytana z PLC. "Dzisiaj" liczone jest
        /// jako roznica wzgledem Baseline_Dzisiaj, ustawianego przy resecie zajec.
        /// </summary>
        public void ZapiszWyprodukowanoOgolem(int suma)
        {
            using var conn = OpenConnection();
            using var cmd = new SqlCommand(@"
                UPDATE Ustawienia_Maszyny
                SET Wyprodukowano_Ogolem = @S,
                    -- Gdyby PLC wyzerowalo licznik, baseline nie moze zostac wyzszy niz stan biezacy.
                    Baseline_Dzisiaj = CASE WHEN Baseline_Dzisiaj > @S THEN @S ELSE Baseline_Dzisiaj END", conn);
            cmd.Parameters.AddWithValue("@S", suma);
            cmd.ExecuteNonQuery();
        }

        // ============================================================
        // RESET ZLECEN — flaga ustawiana przez przycisk "Rozpocznij nowe zajecia"
        // ============================================================

        /// <summary>
        ///  Sprawdza i atomowo zeruje flage Wymagany_Reset (odczyt+konsumpcja w 1 zapytaniu,
        ///  zeby dwa cykle pollingu nie wyslaly triggera dwa razy).
        /// </summary>
        public bool CheckAndClearResetRequested()
        {
            const string sql = @"
                UPDATE Ustawienia_Maszyny
                SET Wymagany_Reset = 0
                OUTPUT DELETED.Wymagany_Reset
                WHERE Wymagany_Reset = 1";

            using var conn = OpenConnection();
            using var cmd  = new SqlCommand(sql, conn);
            var result = cmd.ExecuteScalar();
            return result != null && Convert.ToInt32(result) == 1;
        }

        // ============================================================
        // SPRZATANIE ZLECEN W PLC
        // ============================================================

        /// <summary>
        /// Zlecenia zamkniete na stronie (Zakonczone/Anulowane), ktorych rekord
        /// w DB3 nie zostal jeszcze wyzerowany.
        /// </summary>
        public List<int> GetZleceniaDoWyczyszczeniaWPlc()
        {
            var lista = new List<int>();
            using var conn = OpenConnection();
            using var cmd = new SqlCommand(@"
                SELECT ID_Zlecenia FROM Zlecenie_Produkcyjne
                WHERE Status_Zlecenia IN ('Zakonczone', 'Anulowane')
                  AND ISNULL(PlcWyczyszczone, 0) = 0", conn);
            using var rdr = cmd.ExecuteReader();
            while (rdr.Read()) lista.Add(rdr.GetInt32(0));
            return lista;
        }

        public void OznaczPlcWyczyszczone(int idZlecenia)
        {
            using var conn = OpenConnection();
            using var cmd = new SqlCommand(
                "UPDATE Zlecenie_Produkcyjne SET PlcWyczyszczone = 1 WHERE ID_Zlecenia = @ID", conn);
            cmd.Parameters.AddWithValue("@ID", idZlecenia);
            cmd.ExecuteNonQuery();
        }

        // ============================================================
        // CYKLE STANOWISK -> HistoriaCykli + wydajnosc na HMI
        // ============================================================

        /// <summary>Ile ostatnich cykli stanowiska wchodzi do wydajnosci pokazywanej na HMI.</summary>
        private const int OKNO_CYKLI_HMI = 4;

        /// <summary>
        /// Zapisuje zmierzony cykl stanowiska i zwraca jego biezaca wydajnosc w calych
        /// procentach (czas zadany / czas rzeczywisty z okna ostatnich cykli) albo null,
        /// gdy nie da sie ustalic wyrobu lub czasu zadanego.
        ///
        /// Czas rzeczywisty mierzy Middleware miedzy przejsciami Production.State 0->1 i 1->0,
        /// bo Realizacja_Produkcji nie dostaje rekordow (martwy wyzwalacz z DB5) - bez tego
        /// wydajnosc stanowiska nie mialaby zadnego zrodla danych.
        /// </summary>
        public int? ZapiszCyklStanowiska(int idStanowiska, int nrZlecenia, int czasCykluMs)
        {
            using var conn = OpenConnection();

            int idWyrobu;
            using (var cmd = new SqlCommand(
                "SELECT ID_Wyrobu FROM Zlecenie_Produkcyjne WHERE ID_Zlecenia = @Z", conn))
            {
                cmd.Parameters.AddWithValue("@Z", nrZlecenia);
                var v = cmd.ExecuteScalar();
                if (v == null || v == DBNull.Value) return null;   // zlecenie juz skasowane
                idWyrobu = Convert.ToInt32(v);
            }

            int czasZadanyMs;
            using (var cmd = new SqlCommand(@"
                SELECT Czas_Jednostkowy FROM Proces_Montazu
                WHERE ID_Wyrobu = @W AND ID_Stanowiska = @S", conn))
            {
                cmd.Parameters.AddWithValue("@W", idWyrobu);
                cmd.Parameters.AddWithValue("@S", idStanowiska);
                var v = cmd.ExecuteScalar();
                czasZadanyMs = (v == null || v == DBNull.Value) ? 0 : Convert.ToInt32(v);
            }

            using (var cmd = new SqlCommand(@"
                INSERT INTO HistoriaCykli (ID_Wyrobu, ID_Stanowiska, Czas_Cyklu_ms, Czas_Zadany_ms)
                VALUES (@W, @S, @C, @Z)", conn))
            {
                cmd.Parameters.AddWithValue("@W", idWyrobu);
                cmd.Parameters.AddWithValue("@S", idStanowiska);
                cmd.Parameters.AddWithValue("@C", czasCykluMs);
                cmd.Parameters.AddWithValue("@Z", czasZadanyMs);
                cmd.ExecuteNonQuery();
            }

            ZapiszWskaznikiCyklu(conn, idStanowiska, nrZlecenia, czasCykluMs, czasZadanyMs);

            if (czasZadanyMs <= 0) return null;   // brak normy - nie ma z czym porownac

            using (var cmd = new SqlCommand($@"
                SELECT SUM(CAST(Czas_Zadany_ms AS float)), SUM(CAST(Czas_Cyklu_ms AS float))
                FROM (
                    SELECT TOP ({OKNO_CYKLI_HMI}) Czas_Zadany_ms, Czas_Cyklu_ms
                    FROM HistoriaCykli
                    WHERE ID_Stanowiska = @S AND Czas_Cyklu_ms > 0 AND Czas_Zadany_ms > 0
                    ORDER BY ID DESC
                ) okno", conn))
            {
                cmd.Parameters.AddWithValue("@S", idStanowiska);
                using var rdr = cmd.ExecuteReader();
                if (!rdr.Read() || rdr.IsDBNull(0) || rdr.IsDBNull(1)) return null;
                double zadany = rdr.GetDouble(0), rzeczywisty = rdr.GetDouble(1);
                if (rzeczywisty <= 0) return null;
                return (int)Math.Round(zadany / rzeczywisty * 100.0);
            }
        }

        // ============================================================
        // WSKAZNIKI OEE
        // ============================================================

        /// <summary>
        /// Luka dluzsza niz to nie jest przestojem linii, tylko przerwa w zajeciach -
        /// nie obciaza dostepnosci. Bez tego omowienie cwiczenia zjadaloby cale OEE.
        /// </summary>
        private const int MAX_LUKA_MS = 5 * 60 * 1000;

        /// <summary>Ile ostatnich cykli stanowiska wchodzi do liczenia dostepnosci.</summary>
        private const int OKNO_DOSTEPNOSCI = 10;

        /// <summary>
        /// Dopisuje wiersz do Wskazniki po zamknietym cyklu stanowiska. Bez tego caly blok
        /// OEE na pulpicie stal na zerach - stary wyzwalacz liczyl wskazniki przy INSERT do
        /// Realizacja_Produkcji, a ta tabela nie dostaje rekordow.
        ///
        /// Metodyka (wariant uzgodniony - dostepnosc z luk miedzy sztukami):
        ///   A = czas pracy / (czas pracy + zmierzone luki miedzy kolejnymi sztukami)
        ///       luki dluzsze niz MAX_LUKA_MS pomijamy - to przerwa w zajeciach, nie przestoj
        ///   P = czas zadany / czas rzeczywisty, przyciety do 100% (tak jak w metodyce OEE)
        ///   Q = SztukOK / (SztukOK + SztukNOK) biezacego zlecenia
        ///   OEE = A x P x Q
        /// </summary>
        private void ZapiszWskaznikiCyklu(SqlConnection conn, int idStanowiska, int nrZlecenia,
                                          int czasCykluMs, int czasZadanyMs)
        {
            // ── Dostepnosc: czas pracy kontra luki miedzy sztukami ──────────
            var cykle = new List<(int cyklMs, DateTime koniec)>();
            using (var cmd = new SqlCommand($@"
                SELECT TOP ({OKNO_DOSTEPNOSCI}) Czas_Cyklu_ms, Czas_Zakonczenia
                FROM HistoriaCykli
                WHERE ID_Stanowiska = @S AND Czas_Cyklu_ms > 0
                ORDER BY ID DESC", conn))
            {
                cmd.Parameters.AddWithValue("@S", idStanowiska);
                using var rdr = cmd.ExecuteReader();
                while (rdr.Read()) cykle.Add((rdr.GetInt32(0), rdr.GetDateTime(1)));
            }

            double pracaMs = cykle.Sum(c => (double)c.cyklMs);
            double lukiMs  = 0;
            // Lista jest od najnowszego: para (i, i+1) to nowszy i starszy cykl.
            for (int i = 0; i + 1 < cykle.Count; i++)
            {
                DateTime startNowszego = cykle[i].koniec.AddMilliseconds(-cykle[i].cyklMs);
                double luka = (startNowszego - cykle[i + 1].koniec).TotalMilliseconds;
                if (luka > 0 && luka <= MAX_LUKA_MS) lukiMs += luka;
            }
            double dostepnosc = (pracaMs + lukiMs) > 0 ? pracaMs / (pracaMs + lukiMs) : 1.0;

            // ── Wydajnosc: norma kontra rzeczywistosc, przycieta do 100% ────
            double wydajnosc = (czasZadanyMs > 0 && czasCykluMs > 0)
                ? Math.Min(1.0, (double)czasZadanyMs / czasCykluMs)
                : 1.0;

            // ── Jakosc: stan zlecenia na ten moment ─────────────────────────
            double jakosc = 1.0;
            using (var cmd = new SqlCommand(
                "SELECT ISNULL(SztukOK,0), ISNULL(SztukNOK,0) FROM Zlecenie_Produkcyjne WHERE ID_Zlecenia = @Z", conn))
            {
                cmd.Parameters.AddWithValue("@Z", nrZlecenia);
                using var rdr = cmd.ExecuteReader();
                if (rdr.Read())
                {
                    int ok = rdr.GetInt32(0), nok = rdr.GetInt32(1);
                    if (ok + nok > 0) jakosc = (double)ok / (ok + nok);
                }
            }

            double oee = dostepnosc * wydajnosc * jakosc;

            using (var cmd = new SqlCommand(@"
                INSERT INTO Wskazniki
                    (ID_Zlecenia, ID_Stanowiska, Wydajnosc, Dostepnosc, Jakosc, Wskaznik_OEE,
                     Czas_Realizacji_ms, Czas_Cyklu_ms, Wskaznik_FTY)
                VALUES (@Z, @S, @P, @A, @Q, @OEE, @Real, @Cykl, @FTY)", conn))
            {
                cmd.Parameters.AddWithValue("@Z",    nrZlecenia);
                cmd.Parameters.AddWithValue("@S",    idStanowiska);
                cmd.Parameters.AddWithValue("@P",    Math.Round(wydajnosc,  4));
                cmd.Parameters.AddWithValue("@A",    Math.Round(dostepnosc, 4));
                cmd.Parameters.AddWithValue("@Q",    Math.Round(jakosc,     4));
                cmd.Parameters.AddWithValue("@OEE",  Math.Round(oee,        4));
                cmd.Parameters.AddWithValue("@Real", czasCykluMs);
                cmd.Parameters.AddWithValue("@Cykl", czasCykluMs);
                cmd.Parameters.AddWithValue("@FTY",  Math.Round(jakosc,     4));
                cmd.ExecuteNonQuery();
            }

            Console.WriteLine($"[INFO] Wskazniki st.{idStanowiska}: A={dostepnosc:P0} P={wydajnosc:P0} "
                            + $"Q={jakosc:P0} -> OEE={oee:P0}");
        }

        private SqlConnection OpenConnection()
        {
            var conn = new SqlConnection(_connectionString);
            conn.Open();
            return conn;
        }
    
        public bool CheckReset() {
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand("SELECT Wymagany_Reset FROM Ustawienia_Maszyny", conn);
            var res = cmd.ExecuteScalar();
            return res != null && Convert.ToInt32(res) == 1;
        }
        public void ClearReset() {
            using var conn = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
            conn.Open();
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand("UPDATE Ustawienia_Maszyny SET Wymagany_Reset = 0", conn);
            cmd.ExecuteNonQuery();
        }
        public void ExecuteNonQuery(string q) {
            try {
                using var conn = new Microsoft.Data.SqlClient.SqlConnection(_connectionString);
                conn.Open();
                using var cmd = new Microsoft.Data.SqlClient.SqlCommand(q, conn);
                cmd.ExecuteNonQuery();
            } catch {}
        }
    }
}
