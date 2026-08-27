-- ================================================================
-- Aktualizacja stanu magazynowego i BOM dla Wyrobow 1-6
-- Zrodlo: "Stan magazynowy klocków.pdf" + "klocki na stanowiskach.pdf"
-- ================================================================
USE BazaDanychRB;
GO

-- ── 1. Dodaj brakujące materiały (kolor/rozmiar nieobecny w katalogu) ──
IF NOT EXISTS (SELECT 1 FROM Material WHERE Wymiary=N'2x1' AND Kolor=N'czerwony' AND Lokalizacja='MAIN')
    INSERT INTO Material (Nazwa_Materialu, Wymiary, TypWysokosci, Kolor, StanBiezacy, Lokalizacja)
    VALUES (N'Klocek 2x1 czerwony', N'2x1', N'wysokie', N'czerwony', 0, 'MAIN');

IF NOT EXISTS (SELECT 1 FROM Material WHERE Wymiary=N'3x1' AND Kolor=N'pomarańczowy' AND Lokalizacja='MAIN')
    INSERT INTO Material (Nazwa_Materialu, Wymiary, TypWysokosci, Kolor, StanBiezacy, Lokalizacja)
    VALUES (N'Klocek 3x1 pomarańczowy', N'3x1', N'wysokie', N'pomarańczowy', 0, 'MAIN');

IF NOT EXISTS (SELECT 1 FROM Material WHERE Wymiary=N'2x1' AND Kolor=N'jasnozielony' AND Lokalizacja='MAIN')
    INSERT INTO Material (Nazwa_Materialu, Wymiary, TypWysokosci, Kolor, StanBiezacy, Lokalizacja)
    VALUES (N'Klocek 2x1 jasnozielony', N'2x1', N'wysokie', N'jasnozielony', 0, 'MAIN');

IF NOT EXISTS (SELECT 1 FROM Material WHERE Wymiary=N'1x1' AND Kolor=N'niebieski' AND Lokalizacja='MAIN')
    INSERT INTO Material (Nazwa_Materialu, Wymiary, TypWysokosci, Kolor, StanBiezacy, Lokalizacja)
    VALUES (N'Klocek 1x1 niebieski', N'1x1', N'wysokie', N'niebieski', 0, 'MAIN');

IF NOT EXISTS (SELECT 1 FROM Material WHERE Wymiary=N'4x1' AND Kolor=N'zielony' AND Lokalizacja='MAIN')
    INSERT INTO Material (Nazwa_Materialu, Wymiary, TypWysokosci, Kolor, StanBiezacy, Lokalizacja)
    VALUES (N'Klocek 4x1 zielony', N'4x1', N'wysokie', N'zielony', 0, 'MAIN');
GO
PRINT N'Brakujące materiały dodane: OK';
GO

-- ── 2. Baseline dla nowo dodanych materiałów (żeby "Reset do stanu bazowego" ich nie pomijał) ──
INSERT INTO InventoryBaseline (ID_Materialu, IloscBazowa, DataBazowa)
SELECT m.ID_Materialu, 0, '2025-07-15'
FROM Material m
WHERE m.Lokalizacja = 'MAIN'
  AND NOT EXISTS (SELECT 1 FROM InventoryBaseline b WHERE b.ID_Materialu = m.ID_Materialu);
GO

-- ── 3. Aktualizacja aktualnego stanu magazynowego ("Stan magazynowy klocków.pdf") ──
UPDATE Material SET StanBiezacy = 5,  AktualizacjaAt = GETDATE()
    WHERE Wymiary=N'16x16' AND Kolor=N'szary'         AND Lokalizacja='MAIN'; -- Stanowisko 1: płyta szara

UPDATE Material SET StanBiezacy = 10, AktualizacjaAt = GETDATE()
    WHERE Wymiary=N'6x16'  AND Kolor=N'czarny'        AND Lokalizacja='MAIN'; -- Stanowisko 1: paletka czarna

UPDATE Material SET StanBiezacy = 70, AktualizacjaAt = GETDATE()
    WHERE Wymiary=N'2x1'   AND Kolor=N'czerwony'      AND Lokalizacja='MAIN'; -- Stanowisko 1: cegła czerwona

UPDATE Material SET StanBiezacy = 40, AktualizacjaAt = GETDATE()
    WHERE Wymiary=N'2x2'   AND Kolor=N'żółty'         AND Lokalizacja='MAIN'; -- Stanowisko 2: cegła żółta

UPDATE Material SET StanBiezacy = 20, AktualizacjaAt = GETDATE()
    WHERE Wymiary=N'3x1'   AND Kolor=N'pomarańczowy'  AND Lokalizacja='MAIN'; -- Stanowisko 2: cegła pomarańczowa

UPDATE Material SET StanBiezacy = 20, AktualizacjaAt = GETDATE()
    WHERE Wymiary=N'2x1'   AND Kolor=N'jasnozielony'  AND Lokalizacja='MAIN'; -- Stanowisko 2: cegła zielona (jasna)

UPDATE Material SET StanBiezacy = 50, AktualizacjaAt = GETDATE()
    WHERE Wymiary=N'1x1'   AND Kolor=N'niebieski'     AND Lokalizacja='MAIN'; -- Stanowisko 3: klocek niebieski

UPDATE Material SET StanBiezacy = 20, AktualizacjaAt = GETDATE()
    WHERE Wymiary=N'2x1'   AND Kolor=N'żółty'         AND Lokalizacja='MAIN'; -- Stanowisko 3: klocek żółty

UPDATE Material SET StanBiezacy = 15, AktualizacjaAt = GETDATE()
    WHERE Wymiary=N'4x1'   AND Kolor=N'zielony'       AND Lokalizacja='MAIN'; -- Stanowisko 3: klocek zielony (ciemny)
GO
PRINT N'Aktualny stan magazynowy zaktualizowany: OK';
GO

-- ── 4. Usuń CAŁĄ starą/błędną BOM dla Wyrobów 1-6 ──
DELETE b FROM Struktura_BOM b
JOIN Wyrob w ON b.ID_Wyrobu = w.ID_Wyrobu
WHERE w.Nazwa_Wyrobu IN (N'Wyrob 1', N'Wyrob 2', N'Wyrob 3', N'Wyrob 4', N'Wyrob 5', N'Wyrob 6');
GO
PRINT N'Stara BOM usunięta: OK';
GO

-- ── 5. Wstaw poprawną BOM dla Wyrobów 1-6 ("klocki na stanowiskach.pdf") ──
DECLARE @W1 int = (SELECT ID_Wyrobu FROM Wyrob WHERE Nazwa_Wyrobu = N'Wyrob 1');
DECLARE @W2 int = (SELECT ID_Wyrobu FROM Wyrob WHERE Nazwa_Wyrobu = N'Wyrob 2');
DECLARE @W3 int = (SELECT ID_Wyrobu FROM Wyrob WHERE Nazwa_Wyrobu = N'Wyrob 3');
DECLARE @W4 int = (SELECT ID_Wyrobu FROM Wyrob WHERE Nazwa_Wyrobu = N'Wyrob 4');
DECLARE @W5 int = (SELECT ID_Wyrobu FROM Wyrob WHERE Nazwa_Wyrobu = N'Wyrob 5');
DECLARE @W6 int = (SELECT ID_Wyrobu FROM Wyrob WHERE Nazwa_Wyrobu = N'Wyrob 6');

DECLARE @PlytaSzara     int = (SELECT ID_Materialu FROM Material WHERE Wymiary=N'16x16' AND Kolor=N'szary'        AND Lokalizacja='MAIN');
DECLARE @PaletkaCzarna  int = (SELECT ID_Materialu FROM Material WHERE Wymiary=N'6x16'  AND Kolor=N'czarny'       AND Lokalizacja='MAIN');
DECLARE @K2x1Czerwony   int = (SELECT ID_Materialu FROM Material WHERE Wymiary=N'2x1'   AND Kolor=N'czerwony'     AND Lokalizacja='MAIN');
DECLARE @K2x2Zolty      int = (SELECT ID_Materialu FROM Material WHERE Wymiary=N'2x2'   AND Kolor=N'żółty'        AND Lokalizacja='MAIN');
DECLARE @K3x1Pomar      int = (SELECT ID_Materialu FROM Material WHERE Wymiary=N'3x1'   AND Kolor=N'pomarańczowy' AND Lokalizacja='MAIN');
DECLARE @K2x1Jasnoziel  int = (SELECT ID_Materialu FROM Material WHERE Wymiary=N'2x1'   AND Kolor=N'jasnozielony' AND Lokalizacja='MAIN');
DECLARE @K1x1Niebieski  int = (SELECT ID_Materialu FROM Material WHERE Wymiary=N'1x1'   AND Kolor=N'niebieski'    AND Lokalizacja='MAIN');
DECLARE @K2x1Zolty      int = (SELECT ID_Materialu FROM Material WHERE Wymiary=N'2x1'   AND Kolor=N'żółty'        AND Lokalizacja='MAIN');
DECLARE @K4x1Zielony    int = (SELECT ID_Materialu FROM Material WHERE Wymiary=N'4x1'   AND Kolor=N'zielony'      AND Lokalizacja='MAIN');

INSERT INTO Struktura_BOM (ID_Wyrobu, ID_Materialu, Ilosc_Sztuk, ID_Stanowiska) VALUES
-- Stanowisko 1
(@W1, @PlytaSzara,    1,  1), (@W1, @PaletkaCzarna, 0, 1), (@W1, @K2x1Czerwony, 19, 1),
(@W2, @PlytaSzara,    1,  1), (@W2, @PaletkaCzarna, 0, 1), (@W2, @K2x1Czerwony, 14, 1),
(@W3, @PlytaSzara,    1,  1), (@W3, @PaletkaCzarna, 0, 1), (@W3, @K2x1Czerwony, 8,  1),
(@W4, @PlytaSzara,    0,  1), (@W4, @PaletkaCzarna, 1, 1), (@W4, @K2x1Czerwony, 7,  1),
(@W5, @PlytaSzara,    0,  1), (@W5, @PaletkaCzarna, 1, 1), (@W5, @K2x1Czerwony, 8,  1),
(@W6, @PlytaSzara,    0,  1), (@W6, @PaletkaCzarna, 1, 1), (@W6, @K2x1Czerwony, 5,  1),
-- Stanowisko 2
(@W1, @K2x2Zolty, 6, 2), (@W1, @K3x1Pomar, 6, 2), (@W1, @K2x1Jasnoziel, 6, 2),
(@W2, @K2x2Zolty, 4, 2), (@W2, @K3x1Pomar, 6, 2), (@W2, @K2x1Jasnoziel, 4, 2),
(@W3, @K2x2Zolty, 6, 2), (@W3, @K3x1Pomar, 8, 2), (@W3, @K2x1Jasnoziel, 9, 2),
(@W4, @K2x2Zolty, 2, 2), (@W4, @K3x1Pomar, 2, 2), (@W4, @K2x1Jasnoziel, 4, 2),
(@W5, @K2x2Zolty, 3, 2), (@W5, @K3x1Pomar, 3, 2), (@W5, @K2x1Jasnoziel, 4, 2),
(@W6, @K2x2Zolty, 2, 2), (@W6, @K3x1Pomar, 5, 2), (@W6, @K2x1Jasnoziel, 2, 2),
-- Stanowisko 3
(@W1, @K1x1Niebieski, 9, 3), (@W1, @K2x1Zolty, 4, 3), (@W1, @K4x1Zielony, 3, 3),
(@W2, @K1x1Niebieski, 9, 3), (@W2, @K2x1Zolty, 6, 3), (@W2, @K4x1Zielony, 5, 3),
(@W3, @K1x1Niebieski, 7, 3), (@W3, @K2x1Zolty, 7, 3), (@W3, @K4x1Zielony, 3, 3),
(@W4, @K1x1Niebieski, 8, 3), (@W4, @K2x1Zolty, 3, 3), (@W4, @K4x1Zielony, 3, 3),
(@W5, @K1x1Niebieski, 10,3), (@W5, @K2x1Zolty, 2, 3), (@W5, @K4x1Zielony, 2, 3),
(@W6, @K1x1Niebieski, 5, 3), (@W6, @K2x1Zolty, 4, 3), (@W6, @K4x1Zielony, 4, 3);
GO
PRINT N'BOM Wyroby 1-6: OK';
GO

-- ── 6. Weryfikacja ──
PRINT N'';
PRINT N'=== Materiały (nowe/zmienione) ===';
SELECT ID_Materialu, Nazwa_Materialu, Wymiary, Kolor, StanBiezacy
FROM Material
WHERE Lokalizacja='MAIN' AND Wymiary IN (N'2x1',N'2x2',N'3x1',N'1x1',N'4x1',N'16x16',N'6x16')
ORDER BY Wymiary, Kolor;

PRINT N'';
PRINT N'=== BOM per Wyrob ===';
SELECT w.Nazwa_Wyrobu, b.ID_Stanowiska, m.Nazwa_Materialu, b.Ilosc_Sztuk
FROM Struktura_BOM b
JOIN Wyrob w ON b.ID_Wyrobu = w.ID_Wyrobu
JOIN Material m ON b.ID_Materialu = m.ID_Materialu
ORDER BY w.Nazwa_Wyrobu, b.ID_Stanowiska;
GO
