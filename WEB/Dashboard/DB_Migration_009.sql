-- ============================================================
--  DB_Migration_009
--  Zlecenie_Produkcyjne.SztukAbort - liczba sztuk przerwanych na linii.
--
--  Potrzebne do FPY (First Pass Yield) w wariancie "przeszla cala linie
--  za pierwszym razem". Sztuka przerwana Abortem na stanowisku 1-3 NIGDY nie
--  dociera do QC, wiec nie trafia ani do SztukOK, ani do SztukNOK - dla Jakosci
--  jest niewidzialna. Bez tej kolumny FPY bylby liczbowo identyczny z Jakoscia
--  (linia nie ma poprawek, NOK sie nie przerabia).
--
--      FPY = SztukOK / (SztukOK + SztukNOK + SztukAbort)
--
--  Skrypt idempotentny.
-- ============================================================

IF COL_LENGTH('dbo.Zlecenie_Produkcyjne', 'SztukAbort') IS NULL
BEGIN
    ALTER TABLE dbo.Zlecenie_Produkcyjne
        ADD SztukAbort INT NOT NULL CONSTRAINT DF_ZP_SztukAbort DEFAULT 0;
END
GO
