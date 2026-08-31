# Lego_Assembly_Line

IP Adres:
PLC             - 192.168.1.1
HMI Assembly 1  - 192.168.1.10
HMI Assembly 2  - 192.168.1.11
HMI Assembly 3  - 192.168.1.12
HMI QC          - 192.168.1.13
Balluff Master  - 192.168.1.20   (BNI XG3-508-0B5-R067)
Banner DXM700   - 192.168.1.30   (zmienione z 192.168.1.20 - konflikt!)

Balluff Camera Config - 192.168.1.40

Balluff:

login:Wojciech
haslo:Mietek.Smietek2000

## Rozkład zmiennych

Uwaga: DB niezoptymalizowany (Optimized access = FALSE) + PUT/GET w CPU, żeby HMI (Kinco) i Blazor widziały adresy. `(Struct)` = zagnieżdżona struktura w TIA; pozostałe grupy (Config, Production) to pola płaskie na jednym poziomie.

### Stanowisko montażowe (assembly)

```
DB_StationsAssembly            (Global DB, Optimized = FALSE)
└─ Station : Array[1..3] of udtStationAssembly

udtStationAssembly
├─ Config
│  ├─ ID : Int
│  ├─ Name : String[20]
│  └─ HourlyRate : Real
├─ Production
│  ├─ State : Int              // 0 none / 1 startOrder / 2 process / 3 finish / 4 fault
│  ├─ ReturnState : Int
│  ├─ OrderNo : Int
│  └─ ProductCode : Int
├─ Timer (Struct)
│  ├─ TargetTime : Time
│  ├─ StartStamp : DTL
│  ├─ RemainingSec : Int
│  └─ RemainingStr : String[6]
├─ PtL_Light : Array[1..3] of Bool
├─ PtL_Touch : Array[1..3] of Bool
├─ Commands (Struct)
│  ├─ Start : Bool
│  ├─ Finish : Bool
│  ├─ ReportFault : Bool
│  └─ AbortProcess : Bool
├─ Sensors (Struct)
│  └─ PalletPresent : Bool
├─ RFID (Struct)
│  ├─ TagPresent : Bool
│  ├─ CmdRead : Bool
│  ├─ CmdWrite : Bool
│  ├─ Status : Int             // 0 OK / 1 error
│  ├─ OrderNo : Int
│  └─ ProductCode : Int
└─ Alarm (Struct)
   ├─ NoPallet : Bool
   ├─ TimeExceeded : Bool
   ├─ OperatorFault : Bool
   └─ RFID_Error : Bool
```

Adresowanie: `Station[1].Commands.Start`, `Station[2].RFID.ProductCode`, `Station[1].PtL_Light[2]`.

### Stanowisko QC

```
DB_StationQC                   (Global DB, Optimized = FALSE)
└─ Station : udtStationQC

udtStationQC
├─ Config
│  ├─ ID : Int
│  ├─ Name : String[20]
│  └─ HourlyRate : Real
├─ Production
│  ├─ State : Int              // 0 none / 1 startOrder / 2 process (inspekcja) / 3 finish / 4 fault
│  ├─ ReturnState : Int
│  ├─ OrderNo : Int
│  └─ ProductCode : Int
├─ Sensors (Struct)
│  └─ PalletPresent : Bool
├─ Camera (Struct)
│  ├─ Trigger : Bool
│  ├─ Ready : Bool
│  ├─ Result : Int             // 0 none / 1 OK / 2 NOK
│  ├─ DefectCode : Int
│  └─ Confidence : Int
├─ Commands (Struct)
│  ├─ ConfirmOK : Bool
│  ├─ ConfirmNOK : Bool
│  ├─ ReportFault : Bool
│  └─ AbortProcess : Bool
├─ Results (Struct)
│  ├─ GoodCount : Int
│  ├─ BadCount : Int
│  ├─ DefectReason : Int
│  └─ DefectCost : Real
├─ RFID (Struct)
│  ├─ TagPresent : Bool
│  ├─ CmdRead : Bool
│  ├─ Status : Int             // 0 OK / 1 error
│  ├─ OrderNo : Int
│  └─ ProductCode : Int
└─ Alarm (Struct)
   ├─ NoPallet : Bool
   ├─ CameraError : Bool
   ├─ RFID_Error : Bool
   └─ OperatorFault : Bool
```

Adresowanie: `Station.Camera.Result`, `Station.Commands.ConfirmNOK`, `Station.Results.DefectReason`.

### Katalog wyrobów

`ProductCode` ze stanowisk i zleceń odnosi się tutaj (nazwa, czas tj, grafika na HMI).

```
DB_Products                    (Global DB, Optimized = FALSE)
└─ Product : Array[1..20] of udtProduct

udtProduct
├─ Code : Int
├─ Name : String[30]
├─ TargetTime : Time          // tj na sztuke
└─ ImageID : Int              // grafika instrukcji na HMI
```

### Zlecenia produkcyjne

```
DB_Orders                      (Global DB, Optimized = FALSE)
└─ Order : Array[1..200] of udtOrder

udtOrder
├─ OrderNo : Int
├─ ProductCode : Int
├─ Quantity : Int
├─ Priority : Int             // 1 najwazniejsze .. 5 najmniej
├─ Status : Int               // 0 new / 1 planned / 2 inProgress / 3 done
├─ SimulationID : Int
├─ SeqNo : Int                // pozycja w kolejce
├─ SchedTimeSum : Real        // suma czasu do szeregowania (ZX) [s]
├─ PlanStart : DTL
├─ PlanEnd : DTL
├─ ActStart : DTL
├─ ActEnd : DTL
├─ GoodCount : Int
├─ BadCount : Int
├─ CostPlanned : Real
├─ CostActual : Real
└─ CostDefective : Real
```

### Poziom linii (harmonogram + maszyna stanów linii)

Typ wewnętrzny PLC: liczy kolejność zleceń i trzyma nadrzędny stan linii. Używany jako `Line` w `DB_Production`.

```
udtLine
├─ LineState : Int             // 0 stop /1 config /2 schedule /3 ready /4 run /5 pause /6 fault /7 done
├─ ScheduleMethod : Int        // 0 SPT / 1 LPT / 2 priority
├─ Queue : Array[1..200] of Int   // kolejnosc realizacji (indeksy do DB_Orders)
├─ QueueLen : Int
├─ CmdCalc : Bool
├─ CmdStart : Bool
├─ CmdStop : Bool
└─ LineRunning : Bool
```

### Interfejs Blazor (web)

Powierzchnia wymiany PLC <-> aplikacja web. Blazor pisze do `FromWeb`, czyta z `ToWeb`.

```
udtWeb
├─ FromWeb (Struct)            // Blazor -> PLC
│  ├─ Feed_OrderNo : Int       // co ma robic stanowisko 1
│  ├─ Feed_ProductCode : Int
│  ├─ Feed_New : Bool          // flaga nowego zlecenia dla st.1
│  ├─ SimulationID : Int
│  ├─ ScheduleMethod : Int     // wybor metody szeregowania
│  ├─ CmdCalcSchedule : Bool
│  ├─ CmdStartLine : Bool
│  └─ CmdStopLine : Bool
└─ ToWeb (Struct)              // PLC -> Blazor
   ├─ Station1_Ack : Bool      // st.1 przyjal zlecenie
   ├─ LineState : Int
   ├─ OrdersDone : Int
   ├─ OrdersInProgress : Int
   └─ OrdersPlanned : Int
```

## Organizacja bloków danych

Wszystko w jednym bloku `DB_Data` (niezoptymalizowany + PUT/GET). Definicje pol UDT (`udtProduct`, `udtOrder`, `udtStationAssembly`, `udtStationQC`, `udtLine`, `udtWeb`) - w sekcjach powyzej.

```
DB_Data                        (Global DB, Optimized = FALSE)
├─ Products : Array[1..20] of udtProduct
├─ Orders   : Array[1..200] of udtOrder
├─ Assembly : Array[1..3] of udtStationAssembly
├─ QC       : udtStationQC
├─ Line     : udtLine
└─ Web      : udtWeb
```

Adresowanie: `DB_Data.Assembly[1].Commands.Start`, `DB_Data.Orders[5].Status`, `DB_Data.Web.FromWeb.Feed_OrderNo`.


