-- ================================================================
-- Naprawa: kodowanie materiałów, wyroby 1-6, BOM Wyrob 2
-- ================================================================
USE BazaDanychRB;
GO

-- ── 1. Naprawa polskich znaków w Material (wymagane N'' prefix) ──
UPDATE Material SET Nazwa_Materialu=N'Płyta 16x16 szara',       Kolor=N'szary'         WHERE ID_Materialu=1;
UPDATE Material SET Nazwa_Materialu=N'Płyta 8x4 niebieska',     Kolor=N'niebieski'     WHERE ID_Materialu=2;
UPDATE Material SET Nazwa_Materialu=N'Klocek 8x2 niebieski',    Kolor=N'niebieski'     WHERE ID_Materialu=3;
UPDATE Material SET Nazwa_Materialu=N'Klocek 8x2 żółty',        Kolor=N'żółty'         WHERE ID_Materialu=4;
UPDATE Material SET Nazwa_Materialu=N'Klocek 8x1 żółty',        Kolor=N'żółty'         WHERE ID_Materialu=5;
UPDATE Material SET Nazwa_Materialu=N'Klocek 8x1 czerwony',     Kolor=N'czerwony'      WHERE ID_Materialu=6;
UPDATE Material SET Nazwa_Materialu=N'Klocek 6x1 żółty',        Kolor=N'żółty'         WHERE ID_Materialu=7;
UPDATE Material SET Nazwa_Materialu=N'Klocek 6x1 czerwony',     Kolor=N'czerwony'      WHERE ID_Materialu=8;
UPDATE Material SET Nazwa_Materialu=N'Klocek 6x2 żółty',        Kolor=N'żółty'         WHERE ID_Materialu=9;
UPDATE Material SET Nazwa_Materialu=N'Klocek 6x2 czerwony',     Kolor=N'czerwony'      WHERE ID_Materialu=10;
UPDATE Material SET Nazwa_Materialu=N'Klocek 6x2 niebieski',    Kolor=N'niebieski'     WHERE ID_Materialu=11;
UPDATE Material SET Nazwa_Materialu=N'Klocek 4x2 czerwony',     Kolor=N'czerwony'      WHERE ID_Materialu=12;
UPDATE Material SET Nazwa_Materialu=N'Klocek 4x2 żółty',        Kolor=N'żółty'         WHERE ID_Materialu=13;
UPDATE Material SET Nazwa_Materialu=N'Klocek 4x2 pomarańczowy', Kolor=N'pomarańczowy'  WHERE ID_Materialu=14;
UPDATE Material SET Nazwa_Materialu=N'Klocek 4x2 jasnozielony', Kolor=N'jasnozielony'  WHERE ID_Materialu=15;
UPDATE Material SET Nazwa_Materialu=N'Klocek 4x2 zielony',      Kolor=N'zielony'       WHERE ID_Materialu=16;
UPDATE Material SET Nazwa_Materialu=N'Klocek 4x1 czerwony',     Kolor=N'czerwony'      WHERE ID_Materialu=17;
UPDATE Material SET Nazwa_Materialu=N'Klocek 4x1 pomarańczowy', Kolor=N'pomarańczowy'  WHERE ID_Materialu=18;
UPDATE Material SET Nazwa_Materialu=N'Klocek 4x1 jasnozielony', Kolor=N'jasnozielony'  WHERE ID_Materialu=19;
UPDATE Material SET Nazwa_Materialu=N'Klocek 3x2 pomarańczowy', Kolor=N'pomarańczowy'  WHERE ID_Materialu=20;
UPDATE Material SET Nazwa_Materialu=N'Klocek 3x2 jasnozielony', Kolor=N'jasnozielony'  WHERE ID_Materialu=21;
UPDATE Material SET Nazwa_Materialu=N'Klocek 3x2 żółty',        Kolor=N'żółty'         WHERE ID_Materialu=22;
UPDATE Material SET Nazwa_Materialu=N'Klocek 3x2 czerwony',     Kolor=N'czerwony'      WHERE ID_Materialu=23;
UPDATE Material SET Nazwa_Materialu=N'Klocek 3x1 brązowy',      Kolor=N'brązowy'       WHERE ID_Materialu=24;
UPDATE Material SET Nazwa_Materialu=N'Klocek 3x1 szary',        Kolor=N'szary'         WHERE ID_Materialu=25;
UPDATE Material SET Nazwa_Materialu=N'Klocek 2x2 zielony',      Kolor=N'zielony'       WHERE ID_Materialu=26;
UPDATE Material SET Nazwa_Materialu=N'Klocek 2x2 żółty',        Kolor=N'żółty'         WHERE ID_Materialu=27;
UPDATE Material SET Nazwa_Materialu=N'Klocek 2x2 czerwony',     Kolor=N'czerwony'      WHERE ID_Materialu=28;
UPDATE Material SET Nazwa_Materialu=N'Klocek 2x1 pomarańczowy', Kolor=N'pomarańczowy'  WHERE ID_Materialu=29;
UPDATE Material SET Nazwa_Materialu=N'Klocek 2x1 niebieski',    Kolor=N'niebieski'     WHERE ID_Materialu=30;
UPDATE Material SET Nazwa_Materialu=N'Klocek 1x1 czerwony',     Kolor=N'czerwony'      WHERE ID_Materialu=31;
UPDATE Material SET Nazwa_Materialu=N'Klocek 1x1 żółty',        Kolor=N'żółty'         WHERE ID_Materialu=32;
UPDATE Material SET Nazwa_Materialu=N'Płyta spec. 16x16 piaskowa',  Kolor=N'piaskowy'      WHERE ID_Materialu=33;
UPDATE Material SET Nazwa_Materialu=N'Płyta 16x16 jasnobrązowa',    Kolor=N'jasnobrązowy'  WHERE ID_Materialu=34;
UPDATE Material SET Nazwa_Materialu=N'Płyta 16x16 jasnozielona',    Kolor=N'jasnozielony'  WHERE ID_Materialu=35;
UPDATE Material SET Nazwa_Materialu=N'Płyta 16x16 ciemnozielona',   Kolor=N'ciemnozielony' WHERE ID_Materialu=36;
UPDATE Material SET Nazwa_Materialu=N'Paletka 6x16 czerwona',       Kolor=N'czerwony'      WHERE ID_Materialu=37;
UPDATE Material SET Nazwa_Materialu=N'Paletka 6x16 czarna',         Kolor=N'czarny'        WHERE ID_Materialu=38;
UPDATE Material SET Nazwa_Materialu=N'Paletka 6x16 niebieska',      Kolor=N'niebieski'     WHERE ID_Materialu=39;
GO

-- Naprawa schowka
UPDATE Material SET Nazwa_Materialu=N'Klocek 2x6 czerwony',  Kolor=N'czerwony'  WHERE ID_Materialu=40;
UPDATE Material SET Nazwa_Materialu=N'Klocek 2x8 niebieski', Kolor=N'niebieski' WHERE ID_Materialu=41;
UPDATE Material SET Nazwa_Materialu=N'Klocek 2x6 żółty',     Kolor=N'żółty'     WHERE ID_Materialu=42;
UPDATE Material SET Nazwa_Materialu=N'Klocek 2x8 żółty',     Kolor=N'żółty'     WHERE ID_Materialu=43;
UPDATE Material SET Nazwa_Materialu=N'Klocek 2x4 żółty',     Kolor=N'żółty'     WHERE ID_Materialu=44;
UPDATE Material SET Nazwa_Materialu=N'Klocek 2x3 żółty',     Kolor=N'żółty'     WHERE ID_Materialu=45;
UPDATE Material SET Nazwa_Materialu=N'Klocek 2x6 niebieski',  Kolor=N'niebieski' WHERE ID_Materialu=46;
GO
PRINT N'Naprawa kodowania materiałów: OK';
GO

-- ── 2. Dodaj brakujące materiały dla Wyrobu 2 ─────────────────────
-- Klocek 8x1 niebieski (potrzebny w BOM Wyrobu 2)
IF NOT EXISTS (SELECT 1 FROM Material WHERE Wymiary='8x1' AND Kolor=N'niebieski' AND Lokalizacja='MAIN')
BEGIN
    INSERT INTO Material (Nazwa_Materialu, Wymiary, TypWysokosci, Kolor, StanBiezacy, Lokalizacja)
    VALUES (N'Klocek 8x1 niebieski', N'8x1', N'wysokie', N'niebieski', 85, 'MAIN');
END

-- Klocek 2x1 żółty (potrzebny w BOM Wyrobu 2)
IF NOT EXISTS (SELECT 1 FROM Material WHERE Wymiary='2x1' AND Kolor=N'żółty' AND Lokalizacja='MAIN')
BEGIN
    INSERT INTO Material (Nazwa_Materialu, Wymiary, TypWysokosci, Kolor, StanBiezacy, Lokalizacja)
    VALUES (N'Klocek 2x1 żółty', N'2x1', N'wysokie', N'żółty', 156, 'MAIN');
END
GO
PRINT N'Dodano brakujące materiały: OK';
GO

-- ── 3. Wyroby 1-6 ─────────────────────────────────────────────────
-- Usuń stare testowe rekordy
DELETE FROM Wyrob WHERE Nazwa_Wyrobu IN ('Wyrob A', 'LEGO Set A');

-- Wstaw Wyrob 1-6
INSERT INTO Wyrob (Nazwa_Wyrobu, Rysunek_Wyrobu) VALUES
(N'Wyrob 1', N'INS-001'),
(N'Wyrob 2', N'INS-002'),
(N'Wyrob 3', N'INS-003'),
(N'Wyrob 4', N'INS-004'),
(N'Wyrob 5', N'INS-005'),
(N'Wyrob 6', N'INS-006');
GO
PRINT N'Wyroby 1-6: OK';
GO

-- ── 4. BOM dla Wyrobu 2 (Instrukcja 2) ────────────────────────────
-- Pobierz ID Wyrobu 2
DECLARE @IDW2 int = (SELECT ID_Wyrobu FROM Wyrob WHERE Nazwa_Wyrobu = N'Wyrob 2');

-- Pobierz ID materiałów
DECLARE @PlytaBrAzowa  int = (SELECT ID_Materialu FROM Material WHERE Nazwa_Materialu = N'Płyta 16x16 jasnobrązowa' AND Lokalizacja='MAIN');
DECLARE @K1x1czerwony  int = (SELECT ID_Materialu FROM Material WHERE Nazwa_Materialu = N'Klocek 1x1 czerwony'      AND Lokalizacja='MAIN');
DECLARE @K8x1niebieski int = (SELECT ID_Materialu FROM Material WHERE Nazwa_Materialu = N'Klocek 8x1 niebieski'    AND Lokalizacja='MAIN');
DECLARE @K4x1czerwony  int = (SELECT ID_Materialu FROM Material WHERE Nazwa_Materialu = N'Klocek 4x1 czerwony'     AND Lokalizacja='MAIN');
DECLARE @K2x1zolty     int = (SELECT ID_Materialu FROM Material WHERE Nazwa_Materialu = N'Klocek 2x1 żółty'        AND Lokalizacja='MAIN');

-- Usuń ewentualne stare wpisy BOM dla Wyrobu 2
DELETE FROM Struktura_BOM WHERE ID_Wyrobu = @IDW2;

-- Wstaw BOM z przypisaniem do stacji:
--   Stacja 1: Brązowa płytka bazowa (16x16 jasnobrązowa) + Czerwony klocek 1x4
--   Stacja 2: Niebieski klocek 1x8 (mamy jako 8x1 niebieski)
--   Stacja 3: Czerwony klocek 1x1 + Żółty klocek 1x2 (mamy jako 2x1 żółty)
INSERT INTO Struktura_BOM (ID_Wyrobu, ID_Materialu, Ilosc_Sztuk, ID_Stanowiska) VALUES
(@IDW2, @PlytaBrAzowa,  1, 1),   -- Stacja 1: Brązowa płytka bazowa 16x16 (1 szt)
(@IDW2, @K4x1czerwony,  1, 1),   -- Stacja 1: Czerwony klocek 1x4 (1 szt)
(@IDW2, @K8x1niebieski, 4, 2),   -- Stacja 2: Niebieski klocek 1x8 (4 szt)
(@IDW2, @K1x1czerwony,  7, 3),   -- Stacja 3: Czerwony klocek 1x1 (7 szt)
(@IDW2, @K2x1zolty,     6, 3);   -- Stacja 3: Żółty klocek 1x2 (6 szt)
GO
PRINT N'BOM Wyrob 2: OK';
GO

-- ── 5. Weryfikacja ────────────────────────────────────────────────
PRINT N'';
PRINT N'=== Wyroby w bazie ===';
SELECT ID_Wyrobu, Nazwa_Wyrobu FROM Wyrob ORDER BY ID_Wyrobu;

PRINT N'';
PRINT N'=== BOM Wyrob 2 ===';
SELECT b.ID_Stanowiska, m.Nazwa_Materialu, b.Ilosc_Sztuk
FROM Struktura_BOM b
JOIN Wyrob w ON b.ID_Wyrobu = w.ID_Wyrobu
JOIN Material m ON b.ID_Materialu = m.ID_Materialu
WHERE w.Nazwa_Wyrobu = N'Wyrob 2'
ORDER BY b.ID_Stanowiska;
GO
