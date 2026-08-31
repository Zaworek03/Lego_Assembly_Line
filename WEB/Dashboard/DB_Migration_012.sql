-- ============================================================
-- 012: powod odrzutu przy werdykcie NOK
-- ------------------------------------------------------------
-- Operator zaznacza powod na HMI; PLC trzyma go w
-- DB_Zlecenia.Zlecenie[x].NOK (bity 140.0-140.5). Powod dotyczy
-- POJEDYNCZEJ SZTUKI, nie calego zlecenia - jedno zlecenie moze
-- miec kilka NOK-ow z roznych przyczyn, dlatego kolumna siedzi
-- przy sztuce, a nie przy zleceniu.
--
-- Kilka zaznaczonych bitow zapisujemy po przecinku w jednym polu.
-- ============================================================
IF COL_LENGTH('SztukiPrzetworzone', 'PowodNOK') IS NULL
    ALTER TABLE SztukiPrzetworzone ADD PowodNOK NVARCHAR(200) NULL;
GO

-- Migawka do raportu: reset zajec kasuje SztukiPrzetworzone, wiec powody
-- trzeba przepisac do raportu w chwili jego tworzenia, inaczej przepadna.
IF COL_LENGTH('RaportZlecenia', 'PowodyNOK') IS NULL
    ALTER TABLE RaportZlecenia ADD PowodyNOK NVARCHAR(400) NULL;
GO
