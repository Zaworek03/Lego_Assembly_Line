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
