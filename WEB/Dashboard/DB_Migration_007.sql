-- ============================================================
--  DB_Migration_007
--  StanowiskoOstatnie - trwala pamiec "co ostatnio bylo na stanowisku".
--  Karty w bloku "Status stanowisk" czerpaly zlecenie/wyrob z aktywnego
--  Zlecenie_Produkcyjne, ktore znika przy "Rozpocznij nowe zajecia" - karty
--  zostawaly wtedy z samymi myslnikami. Ta tabela NIE jest czyszczona przy
--  resecie zajec, wiec ostatni wyrob zostaje widoczny do czasu, az stanowisko
--  faktycznie zacznie robic cos innego.
--  Skrypt idempotentny.
-- ============================================================

IF OBJECT_ID('dbo.StanowiskoOstatnie', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.StanowiskoOstatnie (
        ID_Stanowiska   INT           NOT NULL PRIMARY KEY,
        Nazwa_Zlecenia  NVARCHAR(50)  NULL,
        Nazwa_Wyrobu    NVARCHAR(100) NULL,
        Czas_Cyklu_ms   INT           NULL,
        Czas_Zadany_ms  INT           NULL,
        Wydajnosc       FLOAT         NULL,
        Zaktualizowano  DATETIME      NOT NULL DEFAULT GETDATE(),
        CONSTRAINT FK_SO_Stanowisko FOREIGN KEY (ID_Stanowiska)
            REFERENCES dbo.Stanowisko(ID_Stanowiska)
    );
END
GO
