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

                public void WriteOrderToPlc(int id, int modelId, int partNo, int priority, int index = 0)
        {
            if (_plc == null || !_plc.IsConnected) return;

            // Zgodnie z DB3:
            // DB3.DBW0 = ID (Int)
            // DB3.DBW2 = Model (Int)
            // DB3.DBW4 = PartNo (Int)

            int offset = index * 114;
            _plc.Write("DB3.DBW" + offset, (short)id);
            _plc.Write("DB3.DBW" + (offset + 2), (short)modelId);
            _plc.Write("DB3.DBW" + (offset + 4), (short)partNo);
        }

        public void ResetTrigger()
        {
            _plc.Write($"DB{DB}.DBX{OFF_TRIGGER}.0", false);
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

