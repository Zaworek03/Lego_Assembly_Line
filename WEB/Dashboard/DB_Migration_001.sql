-- =====================================================================
-- MIGRACJA: Rozbudowa MES LiniaProdukcyjnaDashboard
-- Decyzje architektoniczne:
--   * Soft-delete na zleceniach (IsDeleted)
--   * Czas ciągły 24/7 dla backward scheduling
--   * Preempcja priorytetów (status: Wstrzymane)
--   * Zużycie komponentów per stanowisko (ID_Stanowiska w Struktura_BOM)
--   * Automatyczny zwrot rezerw przy usunięciu zlecenia
--   * ZAPAS_SCHOWEK = osobna lokalizacja, tylko ręczny transfer do MAIN
-- =====================================================================

USE BazaDanychRB;
GO

-- ─────────────────────────────────────────────────────────────────────
-- 1. Rozszerzenie tabeli Material (komponenty magazynowe)
-- ─────────────────────────────────────────────────────────────────────
ALTER TABLE [dbo].[Material]
    ADD [Wymiary]            nvarchar(20)  NULL,
        [TypWysokosci]       nvarchar(40)  NULL,  -- wysoki/płaski/paletka/bardziej płaska
        [Kolor]              nvarchar(30)  NULL,
        [StanBiezacy]        int           NOT NULL DEFAULT 0,
        [IloscZarezerwowana] int           NOT NULL DEFAULT 0,
        [Lokalizacja]        nvarchar(20)  NOT NULL DEFAULT 'MAIN', -- MAIN / ZAPAS_SCHOWEK
        [AktualizacjaAt]     datetime      NOT NULL DEFAULT GETDATE();
GO

-- ─────────────────────────────────────────────────────────────────────
-- 2. Rozszerzenie Struktura_BOM — per stanowisko
-- ─────────────────────────────────────────────────────────────────────
ALTER TABLE [dbo].[Struktura_BOM]
    ADD [ID_Stanowiska] int NULL;  -- NULL = wszystkie, konkretny = zużycie na tym stanowisku
GO

-- ─────────────────────────────────────────────────────────────────────
-- 3. Rozszerzenie Proces_Montazu — dodanie TPZ
-- ─────────────────────────────────────────────────────────────────────
ALTER TABLE [dbo].[Proces_Montazu]
    ADD [Czas_Przygotowawczy_ms] int NULL;  -- TPZ jednorazowy
GO

-- ─────────────────────────────────────────────────────────────────────
-- 4. Rozszerzenie Zlecenie_Produkcyjne
-- ─────────────────────────────────────────────────────────────────────
ALTER TABLE [dbo].[Zlecenie_Produkcyjne]
    ADD [Priorytet]          nvarchar(20)  NOT NULL DEFAULT 'Standardowy',
        [PriorytetNum]       tinyint       NOT NULL DEFAULT 2, -- 1=Niski 2=Std 3=Wysoki 4=Krytyczny
        [DueTime]            datetime      NULL,               -- data + godzina wymagalności
        [CreatedAt]          datetime      NOT NULL DEFAULT GETDATE(),
        [StartedAt]          datetime      NULL,
        [CompletedAt]        datetime      NULL,
        [NajpozniejszyStart] datetime      NULL,               -- wynik backward scheduling
        [SztukOK]            int           NOT NULL DEFAULT 0,
        [SztukNOK]           int           NOT NULL DEFAULT 0,
        [IsDeleted]          bit           NOT NULL DEFAULT 0;
GO

-- Aktualizuj istniejące zlecenia o DueTime z Data_Realizacji
UPDATE [dbo].[Zlecenie_Produkcyjne]
SET DueTime = CAST(Data_Realizacji AS datetime)
WHERE Data_Realizacji IS NOT NULL AND DueTime IS NULL;
GO

-- ─────────────────────────────────────────────────────────────────────
-- 5. Nowa tabela: InventoryTransactions
-- ─────────────────────────────────────────────────────────────────────
CREATE TABLE [dbo].[InventoryTransactions] (
    [ID]              int           IDENTITY(1,1) PRIMARY KEY,
    [ID_Materialu]    int           NOT NULL REFERENCES [dbo].[Material]([ID_Materialu]),
    [ID_Zlecenia]     int           NULL,
    [ID_Operatora]    int           NULL,
    [TypTransakcji]   nvarchar(40)  NOT NULL,
    -- Rezerwacja / Zuzycie / ZwrotPoNOK / ResetMagazynu / Transfer / Korekta
    [Ilosc]           int           NOT NULL,  -- dodatnia = przychód, ujemna = rozchód
    [Timestamp]       datetime      NOT NULL DEFAULT GETDATE(),
    [Notatka]         nvarchar(200) NULL
);
GO

-- ─────────────────────────────────────────────────────────────────────
-- 6. Nowa tabela: InventorySnapshots (historia inwentaryzacji)
-- ─────────────────────────────────────────────────────────────────────
CREATE TABLE [dbo].[InventorySnapshots] (
    [ID]           int  IDENTITY(1,1) PRIMARY KEY,
    [ID_Materialu] int  NOT NULL REFERENCES [dbo].[Material]([ID_Materialu]),
    [DataSpisu]    date NOT NULL,
    [Ilosc]        int  NOT NULL
);
GO

-- ─────────────────────────────────────────────────────────────────────
-- 7. Nowa tabela: InventoryBaseline (stan bazowy do resetu)
-- ─────────────────────────────────────────────────────────────────────
CREATE TABLE [dbo].[InventoryBaseline] (
    [ID_Materialu] int  NOT NULL PRIMARY KEY REFERENCES [dbo].[Material]([ID_Materialu]),
    [IloscBazowa]  int  NOT NULL,
    [DataBazowa]   date NOT NULL DEFAULT '2025-07-15'
);
GO

-- ─────────────────────────────────────────────────────────────────────
-- 8. Nowa tabela: ZlecenieMaterialy (wynik eksplozji BOM per zlecenie)
-- ─────────────────────────────────────────────────────────────────────
CREATE TABLE [dbo].[ZlecenieMaterialy] (
    [ID]                   int IDENTITY(1,1) PRIMARY KEY,
    [ID_Zlecenia]          int NOT NULL,
    [ID_Materialu]         int NOT NULL REFERENCES [dbo].[Material]([ID_Materialu]),
    [IloscWymagana]        int NOT NULL,
    [IloscZarezerwowana]   int NOT NULL DEFAULT 0,
    [IloscBrakujaca]       int NOT NULL DEFAULT 0
);
GO

-- ─────────────────────────────────────────────────────────────────────
-- 9. SEED DATA — Materiały LEGO (stan 15.07.2025)
-- ─────────────────────────────────────────────────────────────────────

-- Klocki standardowe
INSERT INTO [dbo].[Material] (Nazwa_Materialu, Wymiary, TypWysokosci, Kolor, StanBiezacy, Lokalizacja) VALUES
('Płyta 16x16 szara',          '16x16', 'płaskie',  'szary',         81,  'MAIN'),
('Płyta 8x4 niebieska',        '8x4',   'płaskie',  'niebieski',     46,  'MAIN'),
('Klocek 8x2 niebieski',       '8x2',   'wysokie',  'niebieski',     80,  'MAIN'),
('Klocek 8x2 żółty',           '8x2',   'wysokie',  'żółty',         118, 'MAIN'),
('Klocek 8x1 żółty',           '8x1',   'wysokie',  'żółty',         119, 'MAIN'),
('Klocek 8x1 czerwony',        '8x1',   'wysokie',  'czerwony',      116, 'MAIN'),
('Klocek 6x1 żółty',           '6x1',   'wysokie',  'żółty',         42,  'MAIN'),
('Klocek 6x1 czerwony',        '6x1',   'wysokie',  'czerwony',      45,  'MAIN'),
('Klocek 6x2 żółty',           '6x2',   'wysokie',  'żółty',         87,  'MAIN'),
('Klocek 6x2 czerwony',        '6x2',   'wysokie',  'czerwony',      42,  'MAIN'),
('Klocek 6x2 niebieski',       '6x2',   'wysokie',  'niebieski',     102, 'MAIN'),
('Klocek 4x2 czerwony',        '4x2',   'wysokie',  'czerwony',      51,  'MAIN'),
('Klocek 4x2 żółty',           '4x2',   'wysokie',  'żółty',         73,  'MAIN'),
('Klocek 4x2 pomarańczowy',    '4x2',   'wysokie',  'pomarańczowy',  31,  'MAIN'),
('Klocek 4x2 jasnozielony',    '4x2',   'wysokie',  'jasnozielony',  53,  'MAIN'),
('Klocek 4x2 zielony',         '4x2',   'wysokie',  'zielony',       45,  'MAIN'),
('Klocek 4x1 czerwony',        '4x1',   'wysokie',  'czerwony',      110, 'MAIN'),
('Klocek 4x1 pomarańczowy',    '4x1',   'wysokie',  'pomarańczowy',  23,  'MAIN'),
('Klocek 4x1 jasnozielony',    '4x1',   'wysokie',  'jasnozielony',  89,  'MAIN'),
('Klocek 3x2 pomarańczowy',    '3x2',   'wysokie',  'pomarańczowy',  67,  'MAIN'),
('Klocek 3x2 jasnozielony',    '3x2',   'wysokie',  'jasnozielony',  28,  'MAIN'),
('Klocek 3x2 żółty',           '3x2',   'wysokie',  'żółty',         97,  'MAIN'),
('Klocek 3x2 czerwony',        '3x2',   'wysokie',  'czerwony',      38,  'MAIN'),
('Klocek 3x1 brązowy',         '3x1',   'wysokie',  'brązowy',       118, 'MAIN'),
('Klocek 3x1 szary',           '3x1',   'wysokie',  'szary',         128, 'MAIN'),
('Klocek 2x2 zielony',         '2x2',   'wysokie',  'zielony',       84,  'MAIN'),
('Klocek 2x2 żółty',           '2x2',   'wysokie',  'żółty',         218, 'MAIN'),
('Klocek 2x2 czerwony',        '2x2',   'wysokie',  'czerwony',      181, 'MAIN'),
('Klocek 2x1 pomarańczowy',    '2x1',   'wysokie',  'pomarańczowy',  194, 'MAIN'),
('Klocek 2x1 niebieski',       '2x1',   'wysokie',  'niebieski',     192, 'MAIN'),
('Klocek 1x1 czerwony',        '1x1',   'wysokie',  'czerwony',      220, 'MAIN'),
('Klocek 1x1 żółty',           '1x1',   'wysokie',  'żółty',         213, 'MAIN'),
-- Elementy INNE
('Płyta spec. 16x16 piaskowa',  '16x16', 'bardziej płaska', 'piaskowy',     6,  'MAIN'),
('Płyta 16x16 jasnobrązowa',    '16x16', 'płaskie',         'jasnobrązowy', 5,  'MAIN'),
('Płyta 16x16 jasnozielona',    '16x16', 'płaskie',         'jasnozielony', 4,  'MAIN'),
('Płyta 16x16 ciemnozielona',   '16x16', 'płaskie',         'ciemnozielony',5,  'MAIN'),
('Paletka 6x16 czerwona',       '6x16',  'paletka',         'czerwony',     20, 'MAIN'),
('Paletka 6x16 czarna',         '6x16',  'paletka',         'czarny',       13, 'MAIN'),
('Paletka 6x16 niebieska',      '6x16',  'paletka',         'niebieski',    9,  'MAIN'),
-- Zapas w schowku
('Klocek 2x6 czerwony',        '2x6', 'wysokie', 'czerwony',     81, 'ZAPAS_SCHOWEK'),
('Klocek 2x8 niebieski',       '2x8', 'wysokie', 'niebieski',    68, 'ZAPAS_SCHOWEK'),
('Klocek 2x6 żółty',           '2x6', 'wysokie', 'żółty',        79, 'ZAPAS_SCHOWEK'),
('Klocek 2x8 żółty',           '2x8', 'wysokie', 'żółty',        7,  'ZAPAS_SCHOWEK'),
('Klocek 2x4 żółty',           '2x4', 'wysokie', 'żółty',        30, 'ZAPAS_SCHOWEK'),
('Klocek 2x3 żółty',           '2x3', 'wysokie', 'żółty',        21, 'ZAPAS_SCHOWEK'),
('Klocek 2x6 niebieski',       '2x6', 'wysokie', 'niebieski',    81, 'ZAPAS_SCHOWEK');
GO

-- Baseline (stan 15.07.2025 — tylko MAIN, schowek nie wliczamy do baseline produkcyjnego)
INSERT INTO [dbo].[InventoryBaseline] (ID_Materialu, IloscBazowa, DataBazowa)
SELECT ID_Materialu, StanBiezacy, '2025-07-15'
FROM [dbo].[Material]
WHERE Lokalizacja = 'MAIN';
GO

-- Snapshots historyczne (dane audytowe)
-- Wstawiamy snapshot 15.07.2025 dla MAIN
INSERT INTO [dbo].[InventorySnapshots] (ID_Materialu, DataSpisu, Ilosc)
SELECT ID_Materialu, '2025-07-15', StanBiezacy
FROM [dbo].[Material]
WHERE Lokalizacja = 'MAIN';
GO

PRINT 'Migracja zakonczona pomyslnie.';
GO
