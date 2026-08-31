namespace PlcToDbMiddleware
{

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
