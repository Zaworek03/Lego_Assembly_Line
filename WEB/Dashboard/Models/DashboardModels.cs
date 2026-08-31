namespace LiniaProdukcyjnaDashboard.Models
{
    // ── Powiadomienie ──────────────────────────────────────────────────
    public class Powiadomienie
    {
        public int      ID           { get; set; }
        public string   Typ          { get; set; } = "";
        public string   Tresc        { get; set; } = "";
        public int?     IDZlecenia   { get; set; }
        public int?     IDStanowiska { get; set; }
        public DateTime Utworzono    { get; set; }
        public bool     Przeczytane  { get; set; }
    }

    // ── Tor z pojemnikami na klocki (HMI.Container z PLC) ─────────────
    public class KontenerTor
    {
        public int    IDStanowiska   { get; set; }
        public int    NrToru         { get; set; }
        public string NazwaMaterialu { get; set; } = "";
        public string Kolor          { get; set; } = "";
        /// <summary>Liczba pojemnikow na torze (0..3).</summary>
        public int    Wartosc        { get; set; }

        public bool Pusty     => Wartosc <= 0;
        public bool KonczySie => Wartosc == 1;
    }

    // ── Raport z bloku zajec ──────────────────────────────────────────
    public class Raport
    {
        public int      ID         { get; set; }
        public string   Nazwa      { get; set; } = "";
        public DateTime Utworzono  { get; set; }
        public double   OEE        { get; set; }
        public double   Dostepnosc { get; set; }
        public double   Wydajnosc  { get; set; }
        public double   Jakosc     { get; set; }
        public double   FPY        { get; set; }
        public int      SztukOK    { get; set; }
        public int      SztukNOK   { get; set; }
        /// <summary>Liczone zapytaniem - karty na pulpicie nie doczytuja szczegolow.</summary>
        public int      LiczbaZlecen { get; set; }

        public int    Razem            => SztukOK + SztukNOK;
        public double ProcentOdrzutow  => Razem > 0 ? SztukNOK * 100.0 / Razem : 0;

        public List<RaportZlecenie> Zlecenia  { get; set; } = new();
        public List<RaportMaterial> Materialy { get; set; } = new();
    }

    public class RaportZlecenie
    {
        public string Nazwa      { get; set; } = "";
        public string? Wyrob     { get; set; }
        public string Status     { get; set; } = "";
        public int    IloscSztuk { get; set; }
        public int    SztukOK    { get; set; }
        public int    SztukNOK   { get; set; }

        /// <summary>
        /// Powody odrzutu sztuk NOK, sklejone w chwili tworzenia raportu.
        /// Reset zajec czysci SztukiPrzetworzone, wiec raport musi miec wlasna kopie.
        /// null = nie podano zadnego powodu.
        /// </summary>
        public string? PowodyNOK { get; set; }
    }

    public class RaportMaterial
    {
        public string Nazwa  { get; set; } = "";
        public int    Zuzyto { get; set; }
    }

    // ── Liczniki produkcji (zrodlo: DoneAllTime z PLC) ────────────────
    public class ProdukcjaLicznik
    {
        /// <summary>Laczna produkcja od zawsze (suma DoneAllTime[0..3]).</summary>
        public int Ogolem  { get; set; }
        /// <summary>
        /// Produkcja od resetu wyliczona z licznika PLC (DoneAllTime - baseline).
        /// UWAGA: PLC nie zlicza sztuk odrzuconych na QC, wiec ta liczba to tylko
        /// dolna granica - patrz <see cref="Dzisiaj"/>.
        /// </summary>
        public int DzisiajZPlc { get; set; }

        /// <summary>
        /// Sztuki wykonane w tej sesji. Bierzemy wieksza z dwoch wartosci:
        ///   - licznika PLC (nie liczy odrzutow),
        ///   - sumy werdyktow QC z zlecen (dobre + wadliwe).
        /// Bez tego zlecenie z jedyna sztuka odrzucona dawalo "1 wadliwa z 0 sztuk",
        /// a zabezpieczenie przed dzieleniem przez zero pokazywalo 0% defektow
        /// zamiast prawdziwych 100%.
        /// </summary>
        public int Dzisiaj => Math.Max(DzisiajZPlc, Dobre + Wadliwe);
        /// <summary>Sztuki odrzucone na QC (kasowane przy resecie zajec).</summary>
        public int Wadliwe { get; set; }
        /// <summary>Sztuki, ktore przeszly QC pozytywnie.</summary>
        public int Dobre { get; set; }
        /// <summary>Sztuki przerwane Abortem na stanowisku 1-3 - nigdy nie dojechaly do QC.</summary>
        public int Przerwane { get; set; }

        /// <summary>Udzial wadliwych w produkcji "dzisiaj". Brak produkcji = 0%, nie 100%.</summary>
        public double ProcentWadliwych => Dzisiaj > 0 ? Math.Clamp(Wadliwe * 100.0 / Dzisiaj, 0, 100) : 0;

        /// <summary>
        /// First Pass Yield: sztuki, ktore przeszly CALA linie za pierwszym razem,
        /// podzielone przez wszystkie rozpoczete. Rozni sie od Jakosci o sztuki
        /// przerwane Abortem - te nie docieraja do QC, wiec Jakosc ich nie widzi.
        /// Linia nie ma poprawek, wiec bez tego skladnika FPY = Jakosc.
        /// </summary>
        public int    Rozpoczete => Dobre + Wadliwe + Przerwane;
        public double FPY        => Rozpoczete > 0 ? (double)Dobre / Rozpoczete : 1.0;
    }

    // ── Wydajnosc cyklu per wyrob ──────────────────────────────────────
    public class WyrobCzasCyklu
    {
        public string  Nazwa      { get; set; } = "";
        /// <summary>Suma czasow cyklu w oknie ostatnich cykli tego wyrobu [ms].</summary>
        public double  SumaCzasMs { get; set; }
        /// <summary>Sredni czas cyklu w oknie [ms] - to trafia na kafelek.</summary>
        public double  SredniCyklMs { get; set; }
        /// <summary>Ile cykli faktycznie zlapalo sie w oknie (0 = brak danych).</summary>
        public int     LiczbaCykli  { get; set; }
        /// <summary>Suma czasow zadanych / suma rzeczywistych w oknie. null = brak danych.</summary>
        public double? Wydajnosc  { get; set; }
    }

    // ── Popularnosc wyrobu (% udzialu w produkcji) ─────────────────────
    public class WyrobPopularnosc
    {
        public string Nazwa   { get; set; } = "";
        public int    Ilosc   { get; set; }
        public double Procent { get; set; }
    }

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

        /// <summary>
        /// Ile wierszy Wskazniki weszlo do sredniej. Tabela zeruje sie przy
        /// "Rozpocznij nowe zajecia", wiec bez tego nie da sie odroznic
        /// "OEE wynosi 0%" od "nie ma jeszcze zadnych pomiarow".
        /// </summary>
        public int    LiczbaPomiarow  { get; set; }
    }

    // ── Status stanowiska ─────────────────────────────────────────────
    public class StanowiskoStatus
    {
        public int       IDStanowiska  { get; set; }
        public string    Nazwa         { get; set; } = "";
        public double?   OEE           { get; set; }
        public int?      OstatniCyklMs { get; set; }
        public DateTime? OstatniaCzas  { get; set; }
        public string?   KodPostoju    { get; set; }
        /// <summary>Wydajnosc = suma czasow zadanych / suma czasow rzeczywistych z ostatnich N sztuk na tym stanowisku.</summary>
        public double?   Wydajnosc     { get; set; }
        public string?   NazwaZlecenia { get; set; }
        public string?   NazwaWyrobu   { get; set; }

        /// <summary>Production.State prosto z PLC: 0=bezczynne, 1=montaz, 2=konczenie, 3=awaria.</summary>
        public int       StanProdukcji { get; set; }

        /// <summary>Moment ostatniej zmiany stanu - baza do liczenia czasu na zywo.</summary>
        public DateTime? StanOd        { get; set; }

        public bool Pracuje => StanProdukcji == 1;

        /// <summary>
        /// Czas ostatniego zakonczonego montazu, "zamrozony" w chwili zejscia ze stanu pracy.
        /// Bez tego po zakonczeniu wyswietlalby sie mysnik, bo Realizacja_Produkcji
        /// nie dostaje rekordow (stary mechanizm wyzwalacza z DB5 nie dziala).
        /// </summary>
        public double? CzasZamrozonySek { get; set; }

        /// <summary>
        /// Czas rzeczywisty. Podczas pracy licznik leci zegarem przegladarki - to tylko
        /// oszacowanie, obarczone opoznieniem odpytywania (PLC -> Middleware -> SQL -> strona).
        /// Po zakonczeniu cyklu pierwszenstwo ma ZMIERZONA wartosc (OstatniCyklMs z
        /// HistoriaCykli), wiec licznik "dociaga" do prawdziwego czasu zamiast zostawac
        /// przy wlasnym oszacowaniu. Zamrozona wartosc sluzy juz tylko za pomost na te
        /// ulamki sekundy, zanim pomiar dojedzie do bazy.
        /// </summary>
        /// <summary>Kiedy zarejestrowano OstatniCyklMs - sluzy do odsiania cyklu z POPRZEDNIEJ sztuki.</summary>
        public DateTime? OstatniCyklCzas { get; set; }

        public double? CzasRzeczywistySek
        {
            get
            {
                if (Pracuje && StanOd.HasValue)
                    return Math.Max(0, (DateTime.Now - StanOd.Value).TotalSeconds);

                // Pomiar uznajemy za "ten wlasnie skonczony" tylko wtedy, gdy jest mlodszy
                // niz ostatnia zmiana stanu. Bez tego zaraz po zejsciu z pracy karta na chwile
                // pokazywala czas POPRZEDNIEJ sztuki (stad przeskok np. na 60 s), zanim swiezy
                // pomiar zdazyl dojechac z Middleware do bazy.
                bool pomiarZTegoCyklu = OstatniCyklMs.HasValue
                    && (!StanOd.HasValue || !OstatniCyklCzas.HasValue
                        || OstatniCyklCzas.Value >= StanOd.Value.AddSeconds(-1));

                if (pomiarZTegoCyklu) return OstatniCyklMs!.Value / 1000.0;
                if (CzasZamrozonySek.HasValue) return CzasZamrozonySek;
                return OstatniCyklMs.HasValue ? OstatniCyklMs.Value / 1000.0 : null;
            }
        }

        public string StatusOpis => StanProdukcji switch
        {
            1 => "PRACUJE",
            2 => "KOŃCZY",
            3 => "AWARIA",
            _ => "BEZCZYNNE"
        };

        public string StatusKolor => StanProdukcji switch
        {
            1 => "var(--success)",
            2 => "var(--accent)",
            3 => "var(--danger)",
            _ => "var(--text-muted)"
        };
        /// <summary>Czas zadany (Proces_Montazu) dla ostatniej sztuki - podany 1:1, bez przeliczen.</summary>
        public int?      OstatniCzasZadanyMs { get; set; }

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
        /// <summary>Moment nacisniecia Start na stanowisku 1 (ustawia Middleware).</summary>
        public DateTime? StartedAt         { get; set; }
        public DateTime? CompletedAt       { get; set; }
        public int       SztukOK           { get; set; }
        public int       SztukNOK          { get; set; }

        /// <summary>Godzina rozpoczecia/zakonczenia w skrocie - "—" gdy jeszcze nie nastapilo.</summary>
        public string StartGodzina => StartedAt?.ToString("HH:mm") ?? "—";
        public string KoniecGodzina => CompletedAt?.ToString("HH:mm") ?? "—";

        /// <summary>
        /// Sztuki, ktore zeszly juz z linii - z werdyktem QC, obojetnie jakim.
        /// Odrzut tez jest zamknietym etapem produkcji: paletka pojechala dalej,
        /// materialu nie da sie cofnac i zlecenie nie bedzie jej powtarzac.
        /// </summary>
        public int Rozliczone => SztukOK + SztukNOK;

        public double PostepProcent => IloscSztuk > 0
            ? Math.Min(100.0, Rozliczone * 100.0 / IloscSztuk)
            : 0;

        public int Pozostalo => Math.Max(0, IloscSztuk - Rozliczone);

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

        /// <summary>Odrzucone sztuki wraz z powodem zaznaczonym przez operatora na HMI.</summary>
        public List<SztukaNOK>          OdrzuconeSztuki { get; set; } = new();
    }

    /// <summary>Pojedyncza sztuka odrzucona na QC - numer sztuki i powod z HMI.</summary>
    public class SztukaNOK
    {
        public int     PartNo { get; set; }
        public string? Powod  { get; set; }
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
        public int      IloscBazowa      { get; set; }
        /// <summary>Ile sztuk miesci jeden pojemnik na torze stanowiska.</summary>
        public int      PojemnoscPojemnika { get; set; }
        public string   Lokalizacja      { get; set; } = "MAIN";
        public int      Dostepny         => StanBiezacy - IloscZarezerwowana;
        public double   ProcentPelny     => IloscBazowa > 0 ? Math.Min(100.0, StanBiezacy * 100.0 / IloscBazowa) : 100.0;
        /// <summary>Procent stanu liczony po ilosci DOSTEPNEJ (po odjeciu rezerwacji).</summary>
        public double   ProcentDostepny  => IloscBazowa > 0 ? Math.Clamp(Dostepny * 100.0 / IloscBazowa, 0, 100) : 100.0;
        /// <summary>Prog alarmowy: ponizej 26% stanu bazowego.</summary>
        public bool     NiskiStan        => ProcentPelny < 26;
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

    // ── Wybieralne opcje (dla formularzy) ────────────────────────────
    public class SelectItem
    {
        public int    ID    { get; set; }
        public string Nazwa { get; set; } = "";
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



