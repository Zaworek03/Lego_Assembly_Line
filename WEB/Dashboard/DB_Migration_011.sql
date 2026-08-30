-- ============================================================
--  DB_Migration_011
--  Stanowisko.Nr_Zlecenia - numer zlecenia, ktore stanowisko ma AKTUALNIE u siebie
--  (Production.OrderNo z DB_Data).
--
--  Problem: karta stanowiska brala zlecenie i wyrob z globalnego "pierwszego
--  aktywnego zlecenia", wiec przy dwoch sztukach jadacych rownolegle wszystkie
--  cztery stanowiska pokazywaly to samo zlecenie - takze to, ktorego wcale
--  u siebie nie mialy. Middleware i tak czyta OrderNo osobno dla kazdego
--  stanowiska; teraz to zapisuje.
--  Skrypt idempotentny.
-- ============================================================

IF COL_LENGTH('dbo.Stanowisko', 'Nr_Zlecenia') IS NULL
BEGIN
    ALTER TABLE dbo.Stanowisko ADD Nr_Zlecenia INT NULL;
END
GO
