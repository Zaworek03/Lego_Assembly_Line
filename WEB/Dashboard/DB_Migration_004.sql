-- ================================================================
-- Redukcja magazynu do jedynych 9 realnych klockow
-- Zrodlo: "Stan magazynowy klockow v2.pdf" (wymiar + kolor + typ wysokosci + ilosc)
-- Kasuje caly stary katalog (w tym ZAPAS_SCHOWEK - juz nieaktualny) i dopasowuje
-- oznaczenia pozostalych 9 pozycji 1:1 do etykiet z PDF.
-- ================================================================
USE BazaDanychRB;
GO

DECLARE @Keep TABLE (ID int PRIMARY KEY);
INSERT INTO @Keep (ID) VALUES (1),(38),(49),(27),(50),(51),(52),(48),(53);

-- 1. Usun zaleznosci historyczne dla materialow spoza ostatecznej listy 9 klockow
DELETE FROM InventoryTransactions WHERE ID_Materialu NOT IN (SELECT ID FROM @Keep);
DELETE FROM InventorySnapshots    WHERE ID_Materialu NOT IN (SELECT ID FROM @Keep);
DELETE FROM InventoryBaseline     WHERE ID_Materialu NOT IN (SELECT ID FROM @Keep);
GO

-- 2. Usun wszystkie materialy poza ostateczna lista 9 klockow (w tym caly ZAPAS_SCHOWEK)
DELETE FROM Material WHERE ID_Materialu NOT IN (1,38,49,27,50,51,52,48,53);
GO
PRINT N'Katalog zredukowany do 9 pozycji: OK';
GO

-- 3. Dopasuj oznaczenia (Wymiary/TypWysokosci/Kolor/Nazwa) 1:1 z PDF
UPDATE Material SET Nazwa_Materialu = N'16x16 szary płaski',       Wymiary=N'16x16', TypWysokosci=N'płaski', Kolor=N'szary'         WHERE ID_Materialu = 1;
UPDATE Material SET Nazwa_Materialu = N'6x16 czarny płaski',       Wymiary=N'6x16',  TypWysokosci=N'płaski', Kolor=N'czarny'        WHERE ID_Materialu = 38;
UPDATE Material SET Nazwa_Materialu = N'2x1 czerwony wysoki',      Wymiary=N'2x1',   TypWysokosci=N'wysoki', Kolor=N'czerwony'      WHERE ID_Materialu = 49;
UPDATE Material SET Nazwa_Materialu = N'2x2 żółty wysoki',         Wymiary=N'2x2',   TypWysokosci=N'wysoki', Kolor=N'żółty'         WHERE ID_Materialu = 27;
UPDATE Material SET Nazwa_Materialu = N'3x1 pomarańczowy wysoki',  Wymiary=N'3x1',   TypWysokosci=N'wysoki', Kolor=N'pomarańczowy'  WHERE ID_Materialu = 50;
UPDATE Material SET Nazwa_Materialu = N'2x1 jasny zielony wysoki', Wymiary=N'2x1',   TypWysokosci=N'wysoki', Kolor=N'jasny zielony' WHERE ID_Materialu = 51;
UPDATE Material SET Nazwa_Materialu = N'1x1 niebieski wysoki',     Wymiary=N'1x1',   TypWysokosci=N'wysoki', Kolor=N'niebieski'     WHERE ID_Materialu = 52;
UPDATE Material SET Nazwa_Materialu = N'2x1 żółty wysoki',         Wymiary=N'2x1',   TypWysokosci=N'wysoki', Kolor=N'żółty'         WHERE ID_Materialu = 48;
UPDATE Material SET Nazwa_Materialu = N'4x1 zielony wysoki',       Wymiary=N'4x1',   TypWysokosci=N'wysoki', Kolor=N'zielony'       WHERE ID_Materialu = 53;
GO
PRINT N'Oznaczenia dopasowane do PDF: OK';
GO

-- 4. Weryfikacja
SELECT ID_Materialu, Nazwa_Materialu, Wymiary, TypWysokosci, Kolor, StanBiezacy, Lokalizacja
FROM Material ORDER BY ID_Materialu;
GO
