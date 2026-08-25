namespace LiniaProdukcyjnaDashboard.Models
{
    // ── KPI dzienne ──────────────────────────────────────────────────
    public class DailyKpi
    {
        public double OEE             { get; set; }
        public double Dostepnosc      { get; set; }
        public double Wydajnosc       { get; set; }
        public double Jakosc          { get; set; }
        public double FTY             { get; set; }
        public double AvgCyklMs       { get; set; }
        public int    Wyprodukowano   { get; set; }
        public int    LiczbaWadliwych { get; set; }
    }

    // ── Status stanowiska ─────────────────────────────────────────────
    public class StanowiskoStatus
    {
        public int       IDStanowiska  { get; set; }
        public string    Nazwa         { get; set; } = "";
        public string?   ImieNazwisko  { get; set; }
        public double?   OEE           { get; set; }
        public int?      OstatniCyklMs { get; set; }
        public DateTime? OstatniaCzas  { get; set; }
        public string?   KodPostoju    { get; set; }

        public bool IsActive =>
            OstatniaCzas.HasValue &&
            (DateTime.Now - OstatniaCzas.Value).TotalSeconds < 120 &&
            string.IsNullOrEmpty(KodPostoju);
    }

    // ── Punkt na wykresie trendu OEE ──────────────────────────────────
    public class OeeTrendPoint
    {
        public DateTime Czas       { get; set; }
        public string Stanowisko { get; set; } = "";
        public int IDStanowiska { get; set; }
        public double   OEE        { get; set; }
        public double   Dostepnosc { get; set; }
        public double   Wydajnosc  { get; set; }
        public double   Jakosc     { get; set; }
    }

    // ── Przyczyna postoju ─────────────────────────────────────────────
    public class PostojCause
    {
        public string Kod    { get; set; } = "";
        public int    Liczba { get; set; }
    }

    // ── Wiersz tabeli realizacji ──────────────────────────────────────
    public class RealizacjaRow
    {
        public int      ID              { get; set; }
        public DateTime Czas            { get; set; }
        public string Stanowisko { get; set; } = "";
        public int IDStanowiska { get; set; }
        public string   Operator        { get; set; } = "";
        public string   Zlecenie        { get; set; } = "";
        public int      CyklMs          { get; set; }
        public double   OEE             { get; set; }
        public bool     WynikQC         { get; set; }
        public int      LiczbaWadliwych { get; set; }
        public string?  KodPostoju      { get; set; }
    }

    // ── Priorytety zleceń (1 = najniższy, 5 = krytyczny) ──
    public static class Priorytety
    {
        public const string P1 = "1 - Niski";
        public const string P2 = "2 - Niski-Standardowy";
        public const string P3 = "3 - Standardowy";
        public const string P4 = "4 - Wysoki";
        public const string P5 = "5 - Krytyczny";

        public static readonly string[] Wszystkie = { P1, P2, P3, P4, P5 };

        public static int ToNum(string p) => p switch
        {
            P5 => 5,
            P4 => 4,
            P3 => 3,
            P2 => 2,
            _  => 1
        };
    }

    // ── Dane zlecenia ─────────────────────────────────────────────────
    public class ZlecenieVM
    {
        public int       IDZlecenia        { get; set; }
        public string    NazwaZlecenia     { get; set; } = "";
        public int       IloscSztuk        { get; set; }
        public int       Wyprodukowano     { get; set; }  // dobre sztuki (OK po QC)
        public DateTime? DataRealizacji    { get; set; }  // stare pole (date)
        public DateTime? DueTime           { get; set; }  // nowe — datetime z godziną
        public string    StatusZlecenia    { get; set; } = "Nowe";
        public int       CzasPlanowanyMs   { get; set; }
        public string?   NazwaWyrobu       { get; set; }
        public int?      IDWyrobu          { get; set; }
        public string    Priorytet         { get; set; } = Priorytety.P3;
        public DateTime? NajpozniejszyStart { get; set; }
        public DateTime? CompletedAt       { get; set; }
        public int       SztukOK           { get; set; }
        public int       SztukNOK          { get; set; }

        public double PostepProcent => IloscSztuk > 0
            ? Math.Min(100.0, SztukOK * 100.0 / IloscSztuk)
            : 0;

        public int Pozostalo => Math.Max(0, IloscSztuk - SztukOK);

        public bool MoznaRozpoczac => StatusZlecenia == "Nowe" || StatusZlecenia == "Wstrzymane";
        public bool WToku          => StatusZlecenia == "W toku";
        public bool Zakonczone     => StatusZlecenia == "Zakonczone";
        public bool Wstrzymane     => StatusZlecenia == "Wstrzymane";
    }

    // ── Szczegóły zlecenia (pełne dane z BOM) ────────────────────────
    public class ZlecenieDetail : ZlecenieVM
    {
        public List<ZlecenieMaterialVM> Materialy    { get; set; } = new();
        public int                      CalkowityCzasMs { get; set; }
    }

    // ── Materiał zlecenia (wynik eksplozji BOM) ───────────────────────
    public class ZlecenieMaterialVM
    {
        public int    ID_Materialu       { get; set; }
        public string NazwaMaterialu    { get; set; } = "";
        public string Wymiary           { get; set; } = "";
        public string TypWysokosci      { get; set; } = "";
        public string Kolor             { get; set; } = "";
        public int    IloscWymagana     { get; set; }
        public int    IloscZarezerwowana { get; set; }
        public int    IloscBrakujaca    { get; set; }
        public bool   MaBraki           => IloscBrakujaca > 0;
    }

    // ── Komponent magazynowy ──────────────────────────────────────────
    public class InventoryItem
    {
        public int      IDMaterialu       { get; set; }
        public string   NazwaMaterialu   { get; set; } = "";
        public string   Wymiary          { get; set; } = "";
        public string   TypWysokosci     { get; set; } = "";
        public string   Kolor            { get; set; } = "";
        public int      StanBiezacy      { get; set; }
        public int      IloscZarezerwowana { get; set; }
        public string   Lokalizacja      { get; set; } = "MAIN";
        public int      Dostepny         => StanBiezacy - IloscZarezerwowana;
        public bool     NiskiStan        => Dostepny < 10;
    }

    // ── Wynik walidacji dostępności komponentów ───────────────────────
    public class WalidacjaKomponentow
    {
        public bool                      CzyMozna         { get; set; } = true;
        public int                       MaxMozliwaIlosc  { get; set; }
        public List<BrakKomponentu>      Braki            { get; set; } = new();
    }

    public class BrakKomponentu
    {
        public int    IDMaterialu      { get; set; }
        public string NazwaMaterialu  { get; set; } = "";
        public string Wymiary         { get; set; } = "";
        public string Kolor           { get; set; } = "";
        public int    IloscWymagana   { get; set; }
        public int    IloscDostepna   { get; set; }
        public int    IloscBrakujaca  => IloscWymagana - IloscDostepna;
    }

    // ── Transakcja magazynowa ─────────────────────────────────────────
    public class InventoryTransaction
    {
        public int      ID            { get; set; }
        public string   Materiał      { get; set; } = "";
        public string   Typ           { get; set; } = "";
        public int      Ilosc         { get; set; }
        public DateTime Timestamp     { get; set; }
        public string?  Zlecenie      { get; set; }
        public string?  Notatka       { get; set; }
    }

    // ── Zalogowany użytkownik ─────────────────────────────────────────
    public class AppUser
    {
        public int    IDOperatora  { get; set; }
        public string ImieNazwisko { get; set; } = "";
        public string Rola         { get; set; } = "Operator"; // Operator | Supervisor
        public bool   IsSupervisor => Rola == "Supervisor";
    }

    // ── Wpis harmonogramu ─────────────────────────────────────────────
    public class HarmonogramRow
    {
        public int       ID            { get; set; }
        public string    NazwaZlecenia { get; set; } = "";
        public string Stanowisko { get; set; } = "";
        public int IDStanowiska { get; set; }
        public string    Operator      { get; set; } = "";
        public DateTime? CzasRozp      { get; set; }
        public DateTime? CzasZak       { get; set; }
    }

    // ── Wybieralne opcje (dla formularzy) ────────────────────────────
    public class SelectItem
    {
        public int    ID    { get; set; }
        public string Nazwa { get; set; } = "";
    }

    // ── Statystyki operatora ──────────────────────────────────────────
    public class OperatorStats
    {
        public int    CykleDziś          { get; set; }
        public double OEEDziś            { get; set; }
        public double FTYDziś            { get; set; }
        public int    WyprodukowanoDziś   { get; set; }
        public int    WadliweDziś         { get; set; }
        public double AvgCyklMs           { get; set; }
        public List<OeeTrendPoint> Trend  { get; set; } = new();
    }

    // ── Raport zlecenia ───────────────────────────────────────────────
    public class ZlecenieRaportRow
    {
        public int       IDZlecenia     { get; set; }
        public string    NazwaZlecenia  { get; set; } = "";
        public string?   NazwaWyrobu    { get; set; }
        public int       IloscSztuk     { get; set; }
        public int       SztukOK        { get; set; }
        public int       SztukNOK       { get; set; }
        public string    Status         { get; set; } = "";
        public string    Priorytet      { get; set; } = "";
        public DateTime? CreatedAt      { get; set; }
        public DateTime? StartedAt      { get; set; }
        public DateTime? CompletedAt    { get; set; }
        public DateTime? DueTime        { get; set; }
        public long      CzasTrwaniaMs  { get; set; }
        public double    Jakosc         { get; set; }
        public double    Dostepnosc     { get; set; }
        public double    Wydajnosc      { get; set; }
        public double    OEE            => Math.Min(1.0, Dostepnosc * Wydajnosc * Jakosc);
        public TimeSpan  CzasTrwania    => TimeSpan.FromMilliseconds(Math.Max(0, CzasTrwaniaMs));
        public bool      Zakonczone      => Status == "Zakonczone";
        public bool      TerminOK        => !DueTime.HasValue || !CompletedAt.HasValue || CompletedAt.Value <= DueTime.Value;
    }
}



