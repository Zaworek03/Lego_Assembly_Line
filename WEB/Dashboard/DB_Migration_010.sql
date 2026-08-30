-- ============================================================
--  DB_Migration_010
--  HistoriaCykli.ID_Zlecenia - potrzebne do liczenia DOSTEPNOSCI.
--
--  Dostepnosc mierzymy czasem transportu palety miedzy stanowiskami:
--  od zakonczenia stanowiska N do rozpoczecia stanowiska N+1 dla TEJ SAMEJ
--  sztuki. Bez numeru zlecenia nie da sie powiazac cykli w jeden przeplyw -
--  wczesniejsza wersja liczyla przerwy miedzy kolejnymi sztukami na tym samym
--  stanowisku, co przy jednej sztuce dawalo zawsze 100%.
--  Skrypt idempotentny.
-- ============================================================

IF COL_LENGTH('dbo.HistoriaCykli', 'ID_Zlecenia') IS NULL
BEGIN
    ALTER TABLE dbo.HistoriaCykli ADD ID_Zlecenia INT NULL;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_HC_Zlecenie' AND object_id = OBJECT_ID('dbo.HistoriaCykli'))
BEGIN
    CREATE INDEX IX_HC_Zlecenie ON dbo.HistoriaCykli(ID_Zlecenia, ID_Stanowiska);
END
GO
