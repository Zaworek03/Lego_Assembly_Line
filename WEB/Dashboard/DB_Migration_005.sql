-- ================================================================
-- Korekta znaczenia danych magazynowych.
--
-- Dwa arkusze byly opisane odwrotnie:
--   * "Stan magazynowy klocków"      -> to POJEMNOSC JEDNEGO POJEMNIKA
--   * "Ilosc klockow w pojemnikach"  -> to FAKTYCZNY STAN MAGAZYNU
--
-- Dowod: w kazdym wierszu druga liczba jest wieksza od pierwszej, a nie da sie
-- miec w pojemnikach na linii wiecej klockow niz sie posiada. Krotnosci ~2-2,4
-- odpowiadaja liczbie pojemnikow realnie stojacych na torze (tor miesci max 3).
-- ================================================================
USE BazaDanychRB;
GO

IF COL_LENGTH('Material','Pojemnosc_Pojemnika') IS NULL
    ALTER TABLE Material ADD Pojemnosc_Pojemnika int NOT NULL DEFAULT 0;
GO

-- StanBiezacy = ile posiadamy, Pojemnosc_Pojemnika = ile miesci jeden pojemnik
UPDATE Material SET StanBiezacy = 41,  Pojemnosc_Pojemnika = 5   WHERE ID_Materialu = 1;   -- 16x16 szary
UPDATE Material SET StanBiezacy = 29,  Pojemnosc_Pojemnika = 10  WHERE ID_Materialu = 38;  -- 6x16 czarny
UPDATE Material SET StanBiezacy = 151, Pojemnosc_Pojemnika = 70  WHERE ID_Materialu = 49;  -- 2x1 czerwony
UPDATE Material SET StanBiezacy = 90,  Pojemnosc_Pojemnika = 40  WHERE ID_Materialu = 27;  -- 2x2 zolty
UPDATE Material SET StanBiezacy = 48,  Pojemnosc_Pojemnika = 20  WHERE ID_Materialu = 50;  -- 3x1 pomaranczowy
UPDATE Material SET StanBiezacy = 47,  Pojemnosc_Pojemnika = 20  WHERE ID_Materialu = 51;  -- 2x1 jasny zielony
UPDATE Material SET StanBiezacy = 91,  Pojemnosc_Pojemnika = 50  WHERE ID_Materialu = 52;  -- 1x1 niebieski
UPDATE Material SET StanBiezacy = 234, Pojemnosc_Pojemnika = 20  WHERE ID_Materialu = 48;  -- 2x1 zolty
UPDATE Material SET StanBiezacy = 35,  Pojemnosc_Pojemnika = 15  WHERE ID_Materialu = 53;  -- 4x1 zielony
GO

-- Baseline (punkt odniesienia dla paskow % w Magazynie) rowna sie nowemu stanowi
UPDATE b SET b.IloscBazowa = m.StanBiezacy
FROM InventoryBaseline b JOIN Material m ON b.ID_Materialu = m.ID_Materialu;
GO

SELECT ID_Materialu, Nazwa_Materialu, StanBiezacy AS Posiadamy,
       Pojemnosc_Pojemnika AS NaPojemnik,
       CAST(StanBiezacy AS float) / NULLIF(Pojemnosc_Pojemnika,0) AS PojemnikowZapasu
FROM Material ORDER BY ID_Materialu;
GO
