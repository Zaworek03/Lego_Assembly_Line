namespace PlcToDbMiddleware
{
    /// <summary>
    ///  Dane odczytane bezposrednio z bloku DB1 (BazaDanychKomunikacja)
    ///  zgodnie z istniejaca struktura w TIA Portal.
    ///
    ///  MAPA BLOKU DB1 — istniejace pola (nie zmieniac offsetow!):
    ///  +---------+------------+-----------------------+
    ///  | Offset  | Typ S7     | Nazwa w TIA Portal    |
    ///  +---------+------------+-----------------------+
    ///  |  0.0    | Bool       | ZapisDoBazy           |
    ///  |  2.0    | String[50] | Numer_Zlecenia        |
    ///  | 54.0    | DInt       | Numer_Operacji        |
    ///  | 58.0    | String[50] | Numer_Paletki         |
    ///  | 110.0   | String[50] | Stanowisko            |
    ///  | 162.0   | String[50] | Operator              |
    ///  | 214.0   | DInt       | Czas_Cyklu_ms         |
    ///  | 218.0   | Bool       | Wynik_QC              |
    ///  +---------+------------+-----------------------+
    ///  NOWE pola do dodania w TIA Portal po Wynik_QC:
    ///  +---------+------------+-----------------------+
    ///  | 220.0   | DInt       | Ilosc_Wyprodukowanych |
    ///  | 224.0   | DInt       | Liczba_Wadliwych      |
    ///  | 228.0   | String[20] | Kod_Postoju           |
    ///  +---------+------------+-----------------------+
    /// </summary>
    public struct RawPlcData
    {
        /// <summary>offset 2  — String[50] — nazwa/numer zlecenia (lookup do bazy)</summary>
        public string NumerZlecenia;
        /// <summary>offset 110 — String[50] — nazwa stanowiska (lookup do bazy)</summary>
        public string NazwaStanowiska;
        /// <summary>offset 162 — String[50] — imie i nazwisko operatora (lookup do bazy)</summary>
        public string NazwaOperatora;
        /// <summary>offset 214 — DInt — faktyczny czas pracy maszyny [ms]</summary>
        public int CzasCykluMs;
        /// <summary>offset 218.0 — Bool — wynik kontroli jakosci (true=OK)</summary>
        public bool WynikQC;
        /// <summary>offset 220 — DInt — liczba wyprodukowanych sztuk w cyklu (NOWE pole)</summary>
        public int IloscWyprodukowanych;
        /// <summary>offset 224 — DInt — liczba wykrytych wadliwych sztuk (NOWE pole)</summary>
        public int LiczbaWadliwych;
        /// <summary>offset 228 — String[20] — kod przyczyny postoju, pusty = brak (NOWE pole)</summary>
        public string KodPostoju;
    }

    /// <summary>
    ///  Kompletny rekord jednego cyklu produkcyjnego —
    ///  dane z PLC wzbogacone o ID z bazy i obliczenia C# (czas splywu, postoj).
    /// </summary>
    public struct PlcData
    {
        public int       IDZlecenia;
        public int       IDStanowiska;
        public int       IDOperatora;
        public int       CzasCykluMs;
        public int       CzasPlanowanyMs;
        public int       IloscWyprodukowanych;
        public int       LiczbaWadliwych;
        public string?   KodPostoju;
        public bool      WynikQC;
        public DateTime  CzasRozpoczecia;
        public DateTime  CzasZakonczenia;
        public int       CzasSplywuMs;
        public int       CzasPostojuMs;
    }

    public struct StanowiskoData
    {
        public int     ID;
        public string  Nazwa;
        public decimal StawkaAmortyzacyjna;
    }

    public struct OperatorData
    {
        public int     ID;
        public string  ImieNazwisko;
        public decimal StawkaGodzinowa;
    }

    public struct ZlecenieData
    {
        public int    ID;
        public string NazwaZlecenia;
        public int    IloscSztuk;
        public int    CzasPlanowanyMs;
    }
}
