-- ============================================================
--  Skrypt tworzący schemat bazy BazaDanychRB
--  Linia montażowa — PlcToDb Middleware v2.0
--  Kolejność tworzenia uwzględnia zależności FK
-- ============================================================

USE [BazaDanychRB];
GO

-- ============================================================
-- USUNIĘCIE STARYCH TABEL (jeśli istnieją) — w odwrotnej kolejności FK
-- ============================================================
IF OBJECT_ID('dbo.Wskazniki',             'U') IS NOT NULL DROP TABLE [dbo].[Wskazniki];
IF OBJECT_ID('dbo.Koszty',               'U') IS NOT NULL DROP TABLE [dbo].[Koszty];
IF OBJECT_ID('dbo.Realizacja_Produkcji', 'U') IS NOT NULL DROP TABLE [dbo].[Realizacja_Produkcji];
IF OBJECT_ID('dbo.Harmonogram',          'U') IS NOT NULL DROP TABLE [dbo].[Harmonogram];
IF OBJECT_ID('dbo.Proces_Montazu',       'U') IS NOT NULL DROP TABLE [dbo].[Proces_Montazu];
IF OBJECT_ID('dbo.Struktura_BOM',        'U') IS NOT NULL DROP TABLE [dbo].[Struktura_BOM];
IF OBJECT_ID('dbo.Zlecenie_Produkcyjne', 'U') IS NOT NULL DROP TABLE [dbo].[Zlecenie_Produkcyjne];
IF OBJECT_ID('dbo.Material',             'U') IS NOT NULL DROP TABLE [dbo].[Material];
IF OBJECT_ID('dbo.Wyrob',               'U') IS NOT NULL DROP TABLE [dbo].[Wyrob];
IF OBJECT_ID('dbo.Operator',             'U') IS NOT NULL DROP TABLE [dbo].[Operator];
IF OBJECT_ID('dbo.Stanowisko',           'U') IS NOT NULL DROP TABLE [dbo].[Stanowisko];
-- Stara tabela z v1.0 (jeśli istnieje)
IF OBJECT_ID('dbo.Produkcja',            'U') IS NOT NULL DROP TABLE [dbo].[Produkcja];
GO

-- ============================================================
-- 1. STANOWISKO (tabela słownikowa — brak FK)
-- ============================================================
CREATE TABLE [dbo].[Stanowisko] (
    ID_Stanowiska        INT          IDENTITY(1,1) NOT NULL,
    Nazwa_Stanowiska     NVARCHAR(100) NOT NULL,
    Stawka_Amortyzacyjna DECIMAL(10,2)  NULL,          -- PLN/h
    Jednostka_Miary      NVARCHAR(20)   NULL DEFAULT 'PLN/h',
    CONSTRAINT PK_Stanowisko PRIMARY KEY CLUSTERED (ID_Stanowiska)
);
GO

-- ============================================================
-- 2. OPERATOR (tabela słownikowa — brak FK)
-- ============================================================
CREATE TABLE [dbo].[Operator] (
    ID_Operatora         INT          IDENTITY(1,1) NOT NULL,
    Imie_Nazwisko        NVARCHAR(100) NOT NULL,
    Stawka_Godzinowa     DECIMAL(10,2)  NULL,          -- PLN/h
    Poziom_Zaawansowania NVARCHAR(50)   NULL,          -- np. Junior, Senior, Ekspert
    Jednostka_Miary      NVARCHAR(20)   NULL DEFAULT 'PLN/h',
    CONSTRAINT PK_Operator PRIMARY KEY CLUSTERED (ID_Operatora)
);
GO

-- ============================================================
-- 3. WYROB (tabela słownikowa — brak FK)
-- ============================================================
CREATE TABLE [dbo].[Wyrob] (
    ID_Wyrobu            INT          IDENTITY(1,1) NOT NULL,
    Nazwa_Wyrobu         NVARCHAR(100) NOT NULL,
    Rysunek_Wyrobu       NVARCHAR(200)  NULL,          -- nr rysunku / ścieżka do pliku
    Poprawnosc_Wadliwosc NVARCHAR(50)   NULL,          -- status wyrobu
    CONSTRAINT PK_Wyrob PRIMARY KEY CLUSTERED (ID_Wyrobu)
);
GO

-- ============================================================
-- 4. MATERIAL (tabela słownikowa — brak FK)
-- ============================================================
CREATE TABLE [dbo].[Material] (
    ID_Materialu         INT          IDENTITY(1,1) NOT NULL,
    Nazwa_Materialu      NVARCHAR(100) NOT NULL,
    Cena_Jednostkowa     DECIMAL(10,2)  NULL,          -- PLN/j.m.
    Jednostka_Miary      NVARCHAR(20)   NULL DEFAULT 'szt',
    CONSTRAINT PK_Material PRIMARY KEY CLUSTERED (ID_Materialu)
);
GO

-- ============================================================
-- 5. ZLECENIE_PRODUKCYJNE (FK → Wyrob)
-- ============================================================
CREATE TABLE [dbo].[Zlecenie_Produkcyjne] (
    ID_Zlecenia          INT          IDENTITY(1,1) NOT NULL,
    Nazwa_Zlecenia       NVARCHAR(100) NOT NULL,
    Ilosc_Sztuk          INT          NOT NULL DEFAULT 0,
    Data_Realizacji      DATE           NULL,
    ID_Wyrobu            INT            NULL,
    Status_Zlecenia      NVARCHAR(50)   NULL DEFAULT 'Nowe',   -- Nowe / W toku / Zakończone
    Czas_Planowany_ms    INT            NULL,                   -- planowany czas cyklu (ms) — do obliczeń OEE
    CONSTRAINT PK_Zlecenie    PRIMARY KEY CLUSTERED (ID_Zlecenia),
    CONSTRAINT FK_Zl_Wyrob    FOREIGN KEY (ID_Wyrobu) REFERENCES [dbo].[Wyrob](ID_Wyrobu)
);
GO

-- ============================================================
-- 6. STRUKTURA_BOM (FK → Wyrob, Material)
-- ============================================================
CREATE TABLE [dbo].[Struktura_BOM] (
    ID                   INT          IDENTITY(1,1) NOT NULL,
    ID_Wyrobu            INT          NOT NULL,
    ID_Materialu         INT          NOT NULL,
    Ilosc_Sztuk          DECIMAL(10,3) NOT NULL DEFAULT 1,
    CONSTRAINT PK_BOM         PRIMARY KEY CLUSTERED (ID),
    CONSTRAINT FK_BOM_Wyrob   FOREIGN KEY (ID_Wyrobu)   REFERENCES [dbo].[Wyrob](ID_Wyrobu),
    CONSTRAINT FK_BOM_Mat     FOREIGN KEY (ID_Materialu) REFERENCES [dbo].[Material](ID_Materialu)
);
GO

-- ============================================================
-- 7. PROCES_MONTAZU (FK → Wyrob, Stanowisko)
-- ============================================================
CREATE TABLE [dbo].[Proces_Montazu] (
    ID                   INT          IDENTITY(1,1) NOT NULL,
    ID_Wyrobu            INT          NOT NULL,
    ID_Stanowiska        INT          NOT NULL,
    Czas_Jednostkowy     DECIMAL(10,3)  NULL,           -- planowany czas operacji
    Jednostka_Miary      NVARCHAR(20)   NULL DEFAULT 'ms',
    CONSTRAINT PK_Proces      PRIMARY KEY CLUSTERED (ID),
    CONSTRAINT FK_Proc_Wyrob  FOREIGN KEY (ID_Wyrobu)    REFERENCES [dbo].[Wyrob](ID_Wyrobu),
    CONSTRAINT FK_Proc_Stan   FOREIGN KEY (ID_Stanowiska) REFERENCES [dbo].[Stanowisko](ID_Stanowiska)
);
GO

-- ============================================================
-- 8. HARMONOGRAM (FK → Zlecenie, Stanowisko, Operator)
-- ============================================================
CREATE TABLE [dbo].[Harmonogram] (
    ID                   INT      IDENTITY(1,1) NOT NULL,
    ID_Zlecenia          INT      NOT NULL,
    ID_Stanowiska        INT      NOT NULL,
    ID_Operatora         INT      NOT NULL,
    Czas_Rozpoczecia     DATETIME   NULL,
    Czas_Zakonczenia     DATETIME   NULL,
    Jednostka_Miary      NVARCHAR(20) NULL DEFAULT 'ms',
    CONSTRAINT PK_Harm        PRIMARY KEY CLUSTERED (ID),
    CONSTRAINT FK_Harm_Zl     FOREIGN KEY (ID_Zlecenia)   REFERENCES [dbo].[Zlecenie_Produkcyjne](ID_Zlecenia),
    CONSTRAINT FK_Harm_Stan   FOREIGN KEY (ID_Stanowiska)  REFERENCES [dbo].[Stanowisko](ID_Stanowiska),
    CONSTRAINT FK_Harm_Op     FOREIGN KEY (ID_Operatora)   REFERENCES [dbo].[Operator](ID_Operatora)
);
GO

-- ============================================================
-- 9. REALIZACJA_PRODUKCJI — GŁÓWNA TABELA TRANSAKCYJNA (z PLC)
--    (FK → Zlecenie, Stanowisko, Operator)
-- ============================================================
CREATE TABLE [dbo].[Realizacja_Produkcji] (
    ID                      INT      IDENTITY(1,1) NOT NULL,
    ID_Zlecenia             INT      NOT NULL,
    ID_Stanowiska           INT      NOT NULL,
    ID_Operatora            INT      NOT NULL,
    Czas_Rozpoczecia        DATETIME NOT NULL,
    Czas_Zakonczenia        DATETIME NOT NULL,
    Czas_Splywu_ms          INT      NOT NULL,   -- czas między triggerami (mierzony w C#)
    Czas_Cyklu_ms           INT      NOT NULL,   -- czas pracy maszyny (z PLC)
    Czas_Postoju_ms         INT      NOT NULL,   -- obliczony: Splyw - Cykl
    Kod_Postoju             NVARCHAR(50) NULL,   -- kod przyczyny postoju (z PLC lub NULL)
    Ilosc_Wyprodukowanych   INT      NOT NULL DEFAULT 1,
    Liczba_Wadliwych        INT      NOT NULL DEFAULT 0,
    Wynik_QC                BIT      NOT NULL DEFAULT 1,  -- 1=OK, 0=NOK
    CONSTRAINT PK_Real     PRIMARY KEY CLUSTERED (ID),
    CONSTRAINT FK_Real_Zl  FOREIGN KEY (ID_Zlecenia)   REFERENCES [dbo].[Zlecenie_Produkcyjne](ID_Zlecenia),
    CONSTRAINT FK_Real_St  FOREIGN KEY (ID_Stanowiska)  REFERENCES [dbo].[Stanowisko](ID_Stanowiska),
    CONSTRAINT FK_Real_Op  FOREIGN KEY (ID_Operatora)   REFERENCES [dbo].[Operator](ID_Operatora)
);
CREATE INDEX IX_Real_Zlecenie  ON [dbo].[Realizacja_Produkcji](ID_Zlecenia);
CREATE INDEX IX_Real_Czas      ON [dbo].[Realizacja_Produkcji](Czas_Zakonczenia);
GO

-- ============================================================
-- 10. KOSZTY — obliczane po każdym cyklu (FK → Zlecenie, Realizacja)
-- ============================================================
CREATE TABLE [dbo].[Koszty] (
    ID                      INT      IDENTITY(1,1) NOT NULL,
    ID_Zlecenia             INT      NOT NULL,
    ID_Realizacji           INT        NULL,
    Koszt_Materialow        DECIMAL(10,2) NOT NULL DEFAULT 0,
    Koszt_Operatorow        DECIMAL(10,2) NOT NULL DEFAULT 0,
    Koszt_Pracy_Stanowisk   DECIMAL(10,2) NOT NULL DEFAULT 0,
    Koszt_Calkowity         DECIMAL(10,2) NOT NULL DEFAULT 0,
    DataCzas_Kalkulacji     DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT PK_Koszty     PRIMARY KEY CLUSTERED (ID),
    CONSTRAINT FK_Kosz_Zl    FOREIGN KEY (ID_Zlecenia)  REFERENCES [dbo].[Zlecenie_Produkcyjne](ID_Zlecenia),
    CONSTRAINT FK_Kosz_Real  FOREIGN KEY (ID_Realizacji) REFERENCES [dbo].[Realizacja_Produkcji](ID)
);
GO

-- ============================================================
-- 11. WSKAZNIKI — OEE/FTY per cykl (FK → Zlecenie, Realizacja, Stanowisko)
-- ============================================================
CREATE TABLE [dbo].[Wskazniki] (
    ID                        INT      IDENTITY(1,1) NOT NULL,
    ID_Zlecenia               INT      NOT NULL,
    ID_Realizacji             INT        NULL,
    ID_Stanowiska             INT      NOT NULL,
    Wydajnosc                 DECIMAL(8,4) NOT NULL,  -- Performance  [0..1+]
    Dostepnosc                DECIMAL(8,4) NOT NULL,  -- Availability [0..1]
    Jakosc                    DECIMAL(8,4) NOT NULL,  -- Quality       [0..1]
    Wskaznik_OEE              DECIMAL(8,4) NOT NULL,  -- A × P × Q
    Czas_Realizacji_ms        INT      NOT NULL,
    Wydajnosc_Pracy_Operatora DECIMAL(10,4) NULL,     -- szt/h
    Czas_Cyklu_ms             INT      NOT NULL,
    Wskaznik_FTY              DECIMAL(8,4) NOT NULL,  -- First Time Yield
    DataCzas_Pomiaru          DATETIME NOT NULL DEFAULT GETDATE(),
    CONSTRAINT PK_Wsk        PRIMARY KEY CLUSTERED (ID),
    CONSTRAINT FK_Wsk_Zl     FOREIGN KEY (ID_Zlecenia)   REFERENCES [dbo].[Zlecenie_Produkcyjne](ID_Zlecenia),
    CONSTRAINT FK_Wsk_Real   FOREIGN KEY (ID_Realizacji)  REFERENCES [dbo].[Realizacja_Produkcji](ID),
    CONSTRAINT FK_Wsk_Stan   FOREIGN KEY (ID_Stanowiska)  REFERENCES [dbo].[Stanowisko](ID_Stanowiska)
);
CREATE INDEX IX_Wsk_Zlecenie ON [dbo].[Wskazniki](ID_Zlecenia);
CREATE INDEX IX_Wsk_Czas     ON [dbo].[Wskazniki](DataCzas_Pomiaru);
GO

-- ============================================================
-- DANE STARTOWE — 4 stanowiska (uzupełnij stawki!)
-- ============================================================
SET IDENTITY_INSERT [dbo].[Stanowisko] ON;
INSERT INTO [dbo].[Stanowisko] (ID_Stanowiska, Nazwa_Stanowiska, Stawka_Amortyzacyjna, Jednostka_Miary)
VALUES
    (1, 'Stanowisko Montaz 1', 50.00, 'PLN/h'),
    (2, 'Stanowisko Montaz 2', 50.00, 'PLN/h'),
    (3, 'Stanowisko Montaz 3', 50.00, 'PLN/h'),
    (4, 'Stanowisko QC',       30.00, 'PLN/h');
SET IDENTITY_INSERT [dbo].[Stanowisko] OFF;
GO

PRINT '✓ Schemat bazy BazaDanychRB utworzony pomyślnie!';
PRINT '  Tabele: Stanowisko, Operator, Wyrob, Material, Zlecenie_Produkcyjne,';
PRINT '          Struktura_BOM, Proces_Montazu, Harmonogram,';
PRINT '          Realizacja_Produkcji, Koszty, Wskazniki';
PRINT '';
PRINT 'NASTĘPNE KROKI:';
PRINT '  1. Dodaj operatorów:  INSERT INTO [dbo].[Operator] ...';
PRINT '  2. Dodaj wyroby:      INSERT INTO [dbo].[Wyrob] ...';
PRINT '  3. Dodaj zlecenia:    INSERT INTO [dbo].[Zlecenie_Produkcyjne] ...';
GO
