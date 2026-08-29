using S7.Net;

namespace PlcToDbMiddleware
{
    /// <summary>
    ///  Czyta dane ze sterownika S7-1200, blok DB1 "BazaDanychKomunikacja".
    ///  Offsety dopasowane do istniejacego bloku w TIA Portal.
    /// </summary>
    public class PlcReader : IDisposable
    {
        private readonly Plc _plc;
        private bool _disposed;

        private const int DB = 5;

        // �� Istniejace pola (nie zmieniac) ������������������������������
        private const int OFF_TRIGGER          = 0;   // DBX0.0   Bool
        private const int OFF_NUMER_ZLECENIA   = 2;   // String[50]
        private const int OFF_NUMER_OPERACJI   = 54;  // DInt  (uzywany do logow)
        private const int OFF_NUMER_PALETKI    = 58;  // String[50]
        private const int OFF_STANOWISKO       = 110; // String[50]
        private const int OFF_OPERATOR         = 162; // String[50]
        private const int OFF_CZAS_CYKLU       = 214; // DInt  [ms]
        private const int OFF_WYNIK_QC         = 218; // DBX218.0 Bool

        // �� Nowe pola do dodania w TIA Portal po Wynik_QC ���������������
        private const int OFF_ILOSC_WYPROD     = 220; // DInt
        private const int OFF_LICZBA_WAD       = 224; // DInt
        private const int OFF_KOD_POSTOJU      = 228; // String[20]

        public bool IsConnected => _plc?.IsConnected ?? false;

        public PlcReader(string ipAddress)
        {
            _plc = new Plc(CpuType.S71200, ipAddress, rack: 0, slot: 1);
        }

        public void Connect()
        {
            _plc.Open();
            if (!_plc.IsConnected)
                throw new Exception("Polaczenie z PLC nie powiodlo sie. Sprawdz adres IP i dostepnosc sieci.");
            Console.WriteLine("Polaczono z PLC!");
        }

        public bool ReadTrigger()
        {
            return _plc.Read($"DB{DB}.DBX{OFF_TRIGGER}.0") is true;
        }

        public RawPlcData ReadProductionData()
        {
            string ReadStr(int offset, int maxLen)
            {
                var obj = _plc.Read(DataType.DataBlock, DB, offset, VarType.S7String, maxLen);
                return obj as string ?? string.Empty;
            }

            int ReadDInt(int offset)
            {
                var obj = _plc.Read($"DB{DB}.DBD{offset}");
                return obj is uint u ? (int)u : 0;
            }

            return new RawPlcData
            {
                NumerZlecenia        = ReadStr(OFF_NUMER_ZLECENIA, 50),
                NazwaStanowiska      = ReadStr(OFF_STANOWISKO, 50),
                NazwaOperatora       = ReadStr(OFF_OPERATOR, 50),
                CzasCykluMs          = ReadDInt(OFF_CZAS_CYKLU),
                WynikQC              = _plc.Read($"DB{DB}.DBX{OFF_WYNIK_QC}.0") is true,
                // Nowe pola � wymagaja dodania w TIA Portal
                IloscWyprodukowanych = ReadDInt(OFF_ILOSC_WYPROD),
                LiczbaWadliwych      = ReadDInt(OFF_LICZBA_WAD),
                KodPostoju           = ReadStr(OFF_KOD_POSTOJU, 20)
            };
        }

                        // DB_Zlecenia.NastepneZlecenie.Zlecenie — DB3, offsety wg struktury w TIA:
        // ID=27600, Model=27602, PartNo=27604, Priority=27736
        // UWAGA: te offsety zaleza od rozmiaru udtZlecenia w TIA. Rekord urosl kiedys
        // ze 138 na 140 bajtow (dodano Status Array[0..3] of Bool na koncu), co przesunelo
        // cala mape o 400 bajtow. Przy kazdej zmianie UDT trzeba je zaktualizowac -
        // startowa walidacja ponizej wykryje rozjazd i zglosi go w logu.
        private const int OFF_NASTEPNE_BAZA     = 28000;   // NastepneZlecenie.Zlecenie
        private const int OFF_NASTEPNE_ID       = OFF_NASTEPNE_BAZA + 0;
        private const int OFF_NASTEPNE_MODEL    = OFF_NASTEPNE_BAZA + 2;
        private const int OFF_NASTEPNE_PARTNO   = OFF_NASTEPNE_BAZA + 4;
        private const int OFF_NASTEPNE_PRIORITY = OFF_NASTEPNE_BAZA + 136;

        public void WriteOrderToPlc(int id, int modelId, int partNo, int priority)
        {
            if (_plc == null || !_plc.IsConnected) return;

            _plc.Write($"DB3.DBW{OFF_NASTEPNE_ID}",       (short)id);
            _plc.Write($"DB3.DBW{OFF_NASTEPNE_MODEL}",    (short)modelId);
            _plc.Write($"DB3.DBW{OFF_NASTEPNE_PARTNO}",   (short)partNo);
            _plc.Write($"DB3.DBW{OFF_NASTEPNE_PRIORITY}", (short)priority);
        }

        /// <summary>
        /// Sprawdza przy starcie, czy mapa pamieci DB_Zlecenia zgadza sie z offsetami w kodzie.
        /// Zmiana UDT w TIA przesuwa cala mape i zapisy trafiaja wtedy w losowe miejsce,
        /// POZORNIE sie udajac - ta walidacja wychwytuje to od razu, zamiast po cichu psuc dane.
        /// </summary>
        public bool WeryfikujUkladPamieci()
        {
            if (_plc == null || !_plc.IsConnected) return false;
            try
            {
                // ResetZlecen to ostatni bajt bloku - musi byc odczytywalny...
                _plc.Read($"DB3.DBX{OFF_RESET_ZLECEN}.0");

                // ...a odczyt wyraznie za nim musi juz wyjsc poza zakres bloku.
                bool pozaZakresem = false;
                try { _plc.ReadBytes(DataType.DataBlock, 3, OFF_RESET_ZLECEN + 16, 16); }
                catch { pozaZakresem = true; }

                if (!pozaZakresem)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[UWAGA] DB3 siega dalej niz oczekiwany koniec ({OFF_RESET_ZLECEN}). " +
                                      "Prawdopodobnie zmieniono udtZlecenia w TIA - offsety w PlcReader.cs sa nieaktualne!");
                    Console.ResetColor();
                    return false;
                }

                Console.WriteLine($"[INFO] Uklad DB_Zlecenia OK (rekord={ZLECENIE_ELEMENT_SIZE}B, " +
                                  $"NastepneZlecenie={OFF_NASTEPNE_BAZA}, ResetZlecen={OFF_RESET_ZLECEN}.0)");
                return true;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[UWAGA] Nie zgadza sie uklad pamieci DB_Zlecenia: {ex.Message}");
                Console.WriteLine("        Sprawdz offsety w PlcReader.cs wzgledem DB_Zlecenia w TIA.");
                Console.ResetColor();
                return false;
            }
        }

        /// <summary>
        /// Odczytuje ID z NastepneZlecenie. 0 = slot pusty (PLC pobralo zlecenie
        /// na stanowisko albo wyczyscilo tablice po ResetZlecen).
        /// </summary>
        public int ReadNastepneZlecenieId()
        {
            if (_plc == null || !_plc.IsConnected) return -1;
            try
            {
                return (short)((ushort)_plc.Read($"DB3.DBW{OFF_NASTEPNE_ID}"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Nie udalo sie odczytac NastepneZlecenie.ID: {ex.Message}");
                return -1;
            }
        }

        public void WriteWebOrderToPlc(int id, int idWyrobu, int iloscSztuk, int priority)
        {
            if (_plc == null || !_plc.IsConnected) return;

            _plc.Write("DB10.DBW0", (short)id);
            _plc.Write("DB10.DBW2", (short)idWyrobu);
            _plc.Write("DB10.DBW4", (short)iloscSztuk);
            _plc.Write("DB10.DBW6", (short)priority);
        }

        // DB_Zlecenia.ResetZlecen. Zapisujemy tylko TRUE (trigger);
        // PLC ma wlasna logike zerujaca ten bit po wykonaniu resetu.
        private const int OFF_RESET_ZLECEN = 28280;   // za PusteZlecenie (28140 + 140)

        /// <summary>Adres bitu ResetZlecen do logow - zeby komunikat nie rozjechal sie z offsetem.</summary>
        public static string AdresResetZlecen => $"DB3.DBX{OFF_RESET_ZLECEN}.0";

        public void WriteResetZlecen(bool value)
        {
            if (_plc == null || !_plc.IsConnected) return;
            _plc.Write($"DB3.DBX{OFF_RESET_ZLECEN}.0", value);
        }

        
        /// <summary>
        /// Zeruje w DB3 caly rekord zlecenia o podanym ID (140 B). PLC nie czysci
        /// wpisu po zakonczeniu zlecenia na stronie - slot zostaje z ID != 0 i ustawionym
        /// bitem QC, wiec linia dalej "widzi" nieaktualne zlecenie.
        /// Zwraca indeks wyczyszczonego slotu albo -1, gdy zlecenia nie znaleziono.
        /// </summary>
        public int ClearOrderSlot(int idZlecenia)
        {
            if (_plc == null || !_plc.IsConnected || idZlecenia <= 0) return -1;

            byte[] buf;
            try { buf = _plc.ReadBytes(DataType.DataBlock, 3, 0, ZLECENIE_COUNT * ZLECENIE_ELEMENT_SIZE); }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Nie udalo sie odczytac Zlecenie[] przy czyszczeniu: {ex.Message}");
                return -1;
            }

            for (int i = 0; i < ZLECENIE_COUNT; i++)
            {
                int b = i * ZLECENIE_ELEMENT_SIZE;
                short id = (short)((buf[b] << 8) | buf[b + 1]);
                if (id != idZlecenia) continue;

                _plc.WriteBytes(DataType.DataBlock, 3, b, new byte[ZLECENIE_ELEMENT_SIZE]);
                return i;
            }
            return -1;
        }

        /// <summary>Czy w NastepneZlecenie wisi wskazane ID - wtedy tez trzeba je zdjac.</summary>
        public void ClearNastepneZlecenieIf(int idZlecenia)
        {
            if (_plc == null || !_plc.IsConnected) return;
            if (ReadNastepneZlecenieId() != idZlecenia) return;
            _plc.WriteBytes(DataType.DataBlock, 3, OFF_NASTEPNE_BAZA, new byte[ZLECENIE_ELEMENT_SIZE]);
        }
        public (DateTime?, DateTime?) ReadStationTimes(int index, int stationOffset)
        {
            if (_plc == null || !_plc.IsConnected) return (null, null);
            try {
                int baseOffset = index * 130 + stationOffset;
                var startBytes = _plc.ReadBytes(S7.Net.DataType.DataBlock, 3, baseOffset, 12);
                var endBytes = _plc.ReadBytes(S7.Net.DataType.DataBlock, 3, baseOffset + 12, 12);
                DateTime? start = ParseDTL(startBytes);
                DateTime? end = ParseDTL(endBytes);
                return (start, end);
            } catch { return (null, null); }
        }
        private DateTime? ParseDTL(byte[] b) {
            if (b == null || b.Length < 12) return null;
            int year = (b[0] << 8) | b[1];
            if (year < 2000 || year > 2100) return null;
            int month = b[2]; int day = b[3]; int hour = b[5]; int min = b[6]; int sec = b[7];
            uint nano = (uint)((b[8] << 24) | (b[9] << 16) | (b[10] << 8) | b[11]);
            try { return new DateTime(year, month, day, hour, min, sec).AddTicks(nano / 100); } catch { return null; }
        }
        public int FindLatestOrderIndex()
        {
            if (_plc == null || !_plc.IsConnected) return -1;
            for(int i = 0; i < 500; i++) {
                try {
                    var result = _plc.Read("DB3.DBW" + (i * 130));
                    int id = (short)((ushort)result);
                    if (id == 0) return i > 0 ? i - 1 : -1;
                } catch { continue; }
            }
            return 499;
        }
        public int FindFreeOrderIndex()
        {
            if (_plc == null || !_plc.IsConnected) return -1;
            for(int i = 0; i < 500; i++) {
                try {
                    var result = _plc.Read("DB3.DBW" + (i * 130));
                    int id = (short)((ushort)result);
                    if (id == 0) return i;
                } catch { continue; }
            }
            return -1;
        }

        public int ReadBufferId()
        {
            if (_plc == null || !_plc.IsConnected) return -1;
            try
            {
                var result = _plc.Read("DB3.DBW65000");
                return (short)((ushort)result);
            }
            catch
            {
                return -1;
            }
        }

        // DB_Data [DB1] - stan poszczegolnych stanowisk (Production.State/OrderNo per stanowisko).
        // Kazdy blok stanowiska ma 102 bajty, ulozone sekwencyjnie: St1=0, St2=102, St3=204, QC=306.
        // Zweryfikowane empirycznie na zywym PLC (Production.OrderNo zgadzalo sie z aktywnym zleceniem).
        private const int DB_DATA_STANOWISKO_SIZE = 102;
        private const int OFF_DD_PRODUCTION_STATE   = 28; // Int, wzgledem bazy stanowiska
        private const int OFF_DD_PRODUCTION_ORDERNO = 30; // Int, wzgledem bazy stanowiska
        // Stats (struct od +70): DoneToday +70, Efficiency +72, ActualPart +74, Instruction +76.
        // Efficiency to Int, ktory operator widzi na panelu HMI - PLC go nie liczy,
        // ma byc wypelniany z zewnatrz. Zapisujemy tam wydajnosc w calych procentach.
        private const int OFF_DD_EFFICIENCY = 72;

        /// <summary>
        /// Wpisuje wydajnosc stanowiska (cale procenty) do DB_Data.StanowiskoX.Stats.Efficiency,
        /// zeby operator widzial ja na swoim panelu HMI. Numer stanowiska: 1..3 montaz, 4 = QC.
        /// </summary>
        public void WriteEfficiency(int stanowiskoNr, int procent)
        {
            if (_plc == null || !_plc.IsConnected) return;
            if (stanowiskoNr < 1 || stanowiskoNr > 4) return;

            // Int w S7 to 16 bitow ze znakiem - obcinamy do sensownego zakresu,
            // zeby przy dziwnym pomiarze nie wpisac czegos, co przekreci sie na HMI.
            short wartosc = (short)Math.Clamp(procent, 0, 999);
            int baseOff = (stanowiskoNr - 1) * DB_DATA_STANOWISKO_SIZE;
            _plc.Write($"DB1.DBW{baseOff + OFF_DD_EFFICIENCY}", wartosc);
        }

        /// <summary>
        /// Odczytuje Production.State i Production.OrderNo dla stanowiska (1,2,3, 4=QC) z DB_Data.
        /// State: 0=idle, 1=w trakcie montazu, 2=zakonczenie, 3=awaria/abort.
        /// </summary>
        public (int state, int orderNo) ReadStanowiskoProdukcja(int stanowiskoNr)
        {
            if (_plc == null || !_plc.IsConnected) return (0, 0);
            int baseOff = (stanowiskoNr - 1) * DB_DATA_STANOWISKO_SIZE;
            try
            {
                int state   = (short)((ushort)_plc.Read($"DB1.DBW{baseOff + OFF_DD_PRODUCTION_STATE}"));
                int orderNo = (short)((ushort)_plc.Read($"DB1.DBW{baseOff + OFF_DD_PRODUCTION_ORDERNO}"));
                return (state, orderNo);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Nie udalo sie odczytac Production stanowiska {stanowiskoNr}: {ex.Message}");
                return (0, 0);
            }
        }

        // DB_Data.DoneAllTime Array[0..3] of Int - licznik operacji PER STANOWISKO
        // (kazde stanowisko robi +1 dla swojego indeksu po skonczeniu sztuki).
        private const int OFF_DONE_ALL_TIME = 414;
        private const int IDX_DONE_QC       = 3;   // QC = ostatni etap, czyli gotowy wyrob

        /// <summary>
        /// Liczba gotowych wyrobow = licznik stanowiska QC (DoneAllTime[3]).
        /// Suma wszystkich czterech dawalaby ok. 4x za duzo, bo kazda sztuka
        /// przechodzi przez cztery stanowiska i kazde zwieksza swoj licznik.
        /// </summary>
        public int ReadWyprodukowaneWyroby()
        {
            if (_plc == null || !_plc.IsConnected) return -1;
            try
            {
                return (short)((ushort)_plc.Read($"DB1.DBW{OFF_DONE_ALL_TIME + IDX_DONE_QC * 2}"));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Nie udalo sie odczytac DoneAllTime: {ex.Message}");
                return -1;
            }
        }

        // Offsety w rekordzie zlecenia (rel. do poczatku rekordu)
        private const int REL_PARTNO     = 4;
        private const int REL_RETURN_QC  = 134;   // Bool: true = wyrob poprawny
        private const int REL_STATUS     = 138;   // Array[0..3] of Bool w jednym bajcie (bity 0-3)
        private const int BIT_STATUS_QC  = 3;     // Status[3] = stanowisko QC (kolejnosc jak w strukturze Time)

        // udtZlecenia.Time - podstruktura na kazde stanowisko, krok 30 B:
        //   Stanowisko1 = +6, Stanowisko2 = +36, Stanowisko3 = +66, StanowiskoQC = +96.
        // Wewnatrz: StartTime DTL +0 (12 B), EndTime DTL +12 (12 B),
        //           TargetTime Int +24, ActualTime Int +26, Abort Bool +28.
        private const int REL_TIME_BAZA       = 6;
        private const int TIME_STANOWISKO_LEN = 30;
        private const int REL_TIME_TARGET     = 24;
        private const int REL_TIME_ACTUAL     = 26;

        /// <summary>Czasy zadany i rzeczywisty zmierzone PRZEZ PLC dla danego zlecenia i stanowiska (sekundy).</summary>
        public record CzasyStanowiska(int TargetSek, int ActualSek);

        /// <summary>
        /// Odczytuje TargetTime/ActualTime z rekordu zlecenia w DB3. To liczby, ktore
        /// PLC mierzy u siebie - dokladniejsze niz stoper Middleware, ktory po obu stronach
        /// ma niepewnosc rzedu okresu odpytywania.
        /// </summary>
        public CzasyStanowiska? ReadCzasyStanowiska(int idZlecenia, int stanowiskoNr)
        {
            if (_plc == null || !_plc.IsConnected) return null;
            if (idZlecenia <= 0 || stanowiskoNr < 1 || stanowiskoNr > 4) return null;

            byte[] buf;
            try { buf = _plc.ReadBytes(DataType.DataBlock, 3, 0, ZLECENIE_COUNT * ZLECENIE_ELEMENT_SIZE); }
            catch { return null; }

            for (int i = 0; i < ZLECENIE_COUNT; i++)
            {
                int b = i * ZLECENIE_ELEMENT_SIZE;
                short id = (short)((buf[b] << 8) | buf[b + 1]);
                if (id != idZlecenia) continue;

                int t = b + REL_TIME_BAZA + (stanowiskoNr - 1) * TIME_STANOWISKO_LEN;
                short target = (short)((buf[t + REL_TIME_TARGET] << 8) | buf[t + REL_TIME_TARGET + 1]);
                short actual = (short)((buf[t + REL_TIME_ACTUAL] << 8) | buf[t + REL_TIME_ACTUAL + 1]);
                return new CzasyStanowiska(target, actual);
            }
            return null;
        }

        public record SztukaQC(int SlotIndex, int IdZlecenia, int PartNo, bool WynikOK);

        /// <summary>
        /// Liczy sztuki danego zlecenia juz obecne w tablicy Zlecenie[] - czyli takie,
        /// ktore Stanowisko 1 wypuscilo na linie. Potrzebne, by po wyzerowaniu
        /// NastepneZlecenie (co PLC robi po ostatniej sztuce) nie wpisac zlecenia
        /// ponownie i nie wyprodukowac go drugi raz.
        /// </summary>
        public Dictionary<int, int> LiczSztukiWszystkichZlecen()
        {
            var wynik = new Dictionary<int, int>();
            if (_plc == null || !_plc.IsConnected) return wynik;
            try
            {
                var buf = _plc.ReadBytes(DataType.DataBlock, 3, 0, ZLECENIE_COUNT * ZLECENIE_ELEMENT_SIZE);
                for (int i = 0; i < ZLECENIE_COUNT; i++)
                {
                    int b = i * ZLECENIE_ELEMENT_SIZE;
                    short id = (short)((buf[b] << 8) | buf[b + 1]);
                    if (id == 0) continue;
                    wynik[id] = wynik.TryGetValue(id, out var n) ? n + 1 : 1;
                }
                return wynik;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Nie udalo sie policzyc sztuk w tablicy: {ex.Message}");
                return null!;
            }
        }

        /// <summary>
        /// Zwraca sztuki, ktore przeszly przez QC (Status[3] = TRUE), wraz z wynikiem kontroli.
        /// To jedyne wiarygodne zrodlo - stary mechanizm oparty na wyzwalaczu z DB5 nigdy
        /// nie wypelnial danych, przez co QC, postep zlecen i wskaznik defektow stały puste.
        /// </summary>
        public List<SztukaQC> ReadSztukiPoQC()
        {
            var wynik = new List<SztukaQC>();
            if (_plc == null || !_plc.IsConnected) return wynik;

            byte[] buf;
            try
            {
                buf = _plc.ReadBytes(DataType.DataBlock, 3, 0, ZLECENIE_COUNT * ZLECENIE_ELEMENT_SIZE);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Nie udalo sie odczytac tablicy Zlecenie[] (QC): {ex.Message}");
                return wynik;
            }

            for (int i = 0; i < ZLECENIE_COUNT; i++)
            {
                int b = i * ZLECENIE_ELEMENT_SIZE;
                short id = (short)((buf[b] << 8) | buf[b + 1]);
                if (id == 0) continue;

                bool qcSkonczone = (buf[b + REL_STATUS] & (1 << BIT_STATUS_QC)) != 0;
                if (!qcSkonczone) continue;

                short partNo = (short)((buf[b + REL_PARTNO] << 8) | buf[b + REL_PARTNO + 1]);
                bool  ok     = (buf[b + REL_RETURN_QC] & 0x01) != 0;

                wynik.Add(new SztukaQC(i, id, partNo, ok));
            }
            return wynik;
        }

        // DB_Data.StanowiskoX.HMI.Container[1..3].value - liczba pojemnikow na torze (0..3).
        // HMI zaczyna sie na +80 wzgledem stanowiska, Clear na +80, Container[1].value na +82,
        // kolejne co 6 bajtow (value + Add + Subtract).
        private const int OFF_DD_CONTAINER_1 = 82;
        private const int CONTAINER_STRIDE   = 6;

        /// <summary>Migawka calego DB_Data: stany, numery zlecen i kontenery wszystkich stanowisk.</summary>
        public record MigawkaStanowisk(int[] Stany, int[] NumeryZlecen, int[,] Kontenery);

        /// <summary>
        /// Jeden odczyt zamiast kilkunastu osobnych. Wczesniej kazde pole bylo pobierane
        /// oddzielnym zapytaniem do PLC (17 przejsc tam i z powrotem na cykl), przez co
        /// interfejs reagowal z wyraznym opoznieniem.
        /// </summary>
        public MigawkaStanowisk? ReadMigawkaStanowisk()
        {
            if (_plc == null || !_plc.IsConnected) return null;
            try
            {
                // Do konca Production stanowiska QC (306 + 32) z zapasem.
                var buf = _plc.ReadBytes(DataType.DataBlock, 1, 0, 340);
                short W(int off) => (short)((buf[off] << 8) | buf[off + 1]);

                var stany     = new int[4];
                var zlecenia  = new int[4];
                var kontenery = new int[3, 3];

                for (int st = 0; st < 4; st++)
                {
                    int baza = st * DB_DATA_STANOWISKO_SIZE;
                    stany[st]    = W(baza + OFF_DD_PRODUCTION_STATE);
                    zlecenia[st] = W(baza + OFF_DD_PRODUCTION_ORDERNO);

                    if (st < 3)   // QC nie ma torow z klockami
                        for (int tor = 0; tor < 3; tor++)
                            kontenery[st, tor] = W(baza + OFF_DD_CONTAINER_1 + tor * CONTAINER_STRIDE);
                }
                return new MigawkaStanowisk(stany, zlecenia, kontenery);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Nie udalo sie odczytac DB_Data: {ex.Message}");
                return null;
            }
        }

        public void ResetTrigger()
        {
            _plc.Write($"DB{DB}.DBX{OFF_TRIGGER}.0", false);
        }

        // DB_Zlecenia.Zlecenie[0..199] - archiwum zlecen ktore przeszly przez stanowiska.
        // Rozmiar 1 elementu = 138 bajtow (potwierdzone: Zlecenie[1] zaczyna sie na offset 138.0).
        // Abort jest osobnym bitem (bajt) w bloku Time kazdego stanowiska.
        private const int ZLECENIE_ELEMENT_SIZE = 140;   // udtZlecenia: 138 -> 140 (doszedl Status)
        private const int ZLECENIE_COUNT        = 200;
        private const int OFF_ID                = 0;
        private const int OFF_ABORT_ST1         = 34;
        private const int OFF_ABORT_ST2         = 64;
        private const int OFF_ABORT_ST3         = 94;
        private const int OFF_ABORT_QC          = 124;

        /// <summary>
        /// Skanuje cala tablice Zlecenie[] i zwraca zdarzenia Abort=TRUE per stanowisko
        /// (operator porzucil POJEDYNCZA SZTUKE na HMI - dotyczy tylko tej sztuki,
        /// nie calego zlecenia). SlotIndex identyfikuje konkretny wpis w tablicy,
        /// do deduplikacji powiadomien po stronie wywolujacego.
        /// </summary>
        public List<(int slotIndex, int idZlecenia, int stanowiskoNr)> ReadAbortEvents()
        {
            var result = new List<(int, int, int)>();
            if (_plc == null || !_plc.IsConnected) return result;

            byte[] buffer;
            try
            {
                buffer = _plc.ReadBytes(DataType.DataBlock, 3, 0, ZLECENIE_COUNT * ZLECENIE_ELEMENT_SIZE);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[WARN] Nie udalo sie odczytac tablicy Zlecenie[] (Abort scan): {ex.Message}");
                return result;
            }

            for (int i = 0; i < ZLECENIE_COUNT; i++)
            {
                int baseOff = i * ZLECENIE_ELEMENT_SIZE;
                short id = (short)((buffer[baseOff + OFF_ID] << 8) | buffer[baseOff + OFF_ID + 1]);
                if (id == 0) continue;

                if ((buffer[baseOff + OFF_ABORT_ST1] & 0x01) != 0) result.Add((i, id, 1));
                if ((buffer[baseOff + OFF_ABORT_ST2] & 0x01) != 0) result.Add((i, id, 2));
                if ((buffer[baseOff + OFF_ABORT_ST3] & 0x01) != 0) result.Add((i, id, 3));
                if ((buffer[baseOff + OFF_ABORT_QC]  & 0x01) != 0) result.Add((i, id, 4));
            }
            return result;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _plc?.Close();
                _disposed = true;
            }
        }
    }
}



