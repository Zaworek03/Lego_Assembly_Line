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
            const string sql = @"
                SELECT ID_Stanowiska, Nazwa_Stanowiska,
                       ISNULL(Stawka_Amortyzacyjna, 0)
                FROM   [dbo].[Stanowisko]
                WHERE  LOWER(LTRIM(RTRIM(Nazwa_Stanowiska))) = LOWER(LTRIM(RTRIM(@Nazwa)))";

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

        /// <summary>Szuka operatora po imieniu i nazwisku (case-insensitive).</summary>
        public OperatorData GetOperatorByName(string imieNazwisko)
        {
            const string sql = @"
                SELECT ID_Operatora, Imie_Nazwisko,
                       ISNULL(Stawka_Godzinowa, 0)
                FROM   [dbo].[Operator]
                WHERE  LOWER(LTRIM(RTRIM(Imie_Nazwisko))) = LOWER(LTRIM(RTRIM(@Nazwa)))";

            using var conn = OpenConnection();
            using var cmd  = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Nazwa", imieNazwisko);
            using var rdr = cmd.ExecuteReader();

            if (!rdr.Read())
                throw new Exception(
                    $"[DB] Operator '{imieNazwisko}' nie istnieje w tabeli Operator! " +
                    $"Dodaj operatora lub popraw wartosc pola Operator w TIA Portal.");

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

                public List<(int id, int modelId, int partNo, int priority)> GetActiveOrders()
        {
            const string sql = @"
                SELECT TOP 500 ID_Zlecenia, ID_Wyrobu, Ilosc_Sztuk, PriorytetNum
                FROM [dbo].[Zlecenie_Produkcyjne]
                WHERE Status_Zlecenia IN ('W toku', 'Aktywne', 'Oczekujące') 
                  AND IsDeleted = 0
                ORDER BY PriorytetNum DESC, DueTime ASC";

            using var conn = OpenConnection();
            using var cmd = new SqlCommand(sql, conn);
            using var rdr = cmd.ExecuteReader();

            var list = new List<(int, int, int, int)>();
            while (rdr.Read())
            {
                list.Add((Convert.ToInt32(rdr[0]), Convert.ToInt32(rdr[1]), Convert.ToInt32(rdr[2]), Convert.ToInt32(rdr[3])));
            }
            return list;
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

        private SqlConnection OpenConnection()
        {
            var conn = new SqlConnection(_connectionString);
            conn.Open();
            return conn;
        }
    }
}






