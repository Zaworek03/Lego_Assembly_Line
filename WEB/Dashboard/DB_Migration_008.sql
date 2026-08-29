-- ============================================================
--  DB_Migration_008
--  Zlecenie_Produkcyjne.PlcWyczyszczone - znacznik, ze rekord zlecenia
--  zostal juz wyzerowany w DB3 po stronie PLC.
--
--  Problem: gdy zlecenie konczy sie albo zostaje anulowane na stronie, PLC
--  nie czysci swojego wpisu w Zlecenie[] - slot zostaje z ID != 0 i ustawionym
--  bitem QC, wiec linia dalej widzi nieaktualne zlecenie ("TIA twardo stoi
--  przy tym, co bylo"). Middleware zdejmuje takie wpisy, a ta kolumna pilnuje,
--  zeby zrobic to dokladnie raz.
--  Skrypt idempotentny.
-- ============================================================

IF COL_LENGTH('dbo.Zlecenie_Produkcyjne', 'PlcWyczyszczone') IS NULL
BEGIN
    ALTER TABLE dbo.Zlecenie_Produkcyjne
        ADD PlcWyczyszczone BIT NOT NULL CONSTRAINT DF_ZP_PlcWyczyszczone DEFAULT 0;
END
GO
