-- ============================================================
--  DB_Migration_006
--  1) Tabele raportow z zajec (Raporty / RaportZlecenia / RaportMaterialy)
--     - zapisywane przy "Rozpocznij nowe zajecia", zanim dane zostana skasowane
--  2) HistoriaCykli - trwale archiwum czasow cyklu na potrzeby bloku
--     "Wydajnosc cyklu". NIE jest czyszczone przy resecie zajec, dzieki czemu
--     kafelki wydajnosci maja dane od razu po starcie nowych zajec.
--  Skrypt jest idempotentny - mozna go puscic wielokrotnie.
-- ============================================================

IF OBJECT_ID('dbo.Raporty', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.Raporty (
        ID          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Nazwa       NVARCHAR(120) NOT NULL,
        Utworzono   DATETIME      NOT NULL DEFAULT GETDATE(),
        OEE         FLOAT         NOT NULL DEFAULT 0,
        Dostepnosc  FLOAT         NOT NULL DEFAULT 0,
        Wydajnosc   FLOAT         NOT NULL DEFAULT 0,
        Jakosc      FLOAT         NOT NULL DEFAULT 0,
        FPY         FLOAT         NOT NULL DEFAULT 0,
        SztukOK     INT           NOT NULL DEFAULT 0,
        SztukNOK    INT           NOT NULL DEFAULT 0
    );
END
GO

IF OBJECT_ID('dbo.RaportZlecenia', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RaportZlecenia (
        ID          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ID_Raportu  INT           NOT NULL,
        Nazwa       NVARCHAR(50)  NOT NULL,
        Wyrob       NVARCHAR(100) NULL,
        Status      NVARCHAR(30)  NOT NULL,
        IloscSztuk  INT           NOT NULL DEFAULT 0,
        SztukOK     INT           NOT NULL DEFAULT 0,
        SztukNOK    INT           NOT NULL DEFAULT 0,
        CONSTRAINT FK_RZ_Raport FOREIGN KEY (ID_Raportu) REFERENCES dbo.Raporty(ID)
    );
    CREATE INDEX IX_RZ_Raport ON dbo.RaportZlecenia(ID_Raportu);
END
GO

IF OBJECT_ID('dbo.RaportMaterialy', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.RaportMaterialy (
        ID          INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ID_Raportu  INT           NOT NULL,
        Nazwa       NVARCHAR(150) NOT NULL,
        Zuzyto      INT           NOT NULL DEFAULT 0,
        CONSTRAINT FK_RM_Raport FOREIGN KEY (ID_Raportu) REFERENCES dbo.Raporty(ID)
    );
    CREATE INDEX IX_RM_Raport ON dbo.RaportMaterialy(ID_Raportu);
END
GO

-- ------------------------------------------------------------
-- Trwale archiwum cykli. Realizacja_Produkcji jest kasowana przy
-- resecie zajec (FK do Zlecenie_Produkcyjne), wiec przed usunieciem
-- przepisujemy z niej czasy tutaj.
-- ------------------------------------------------------------
IF OBJECT_ID('dbo.HistoriaCykli', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.HistoriaCykli (
        ID               INT      IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ID_Wyrobu        INT      NOT NULL,
        ID_Stanowiska    INT      NOT NULL,
        Czas_Cyklu_ms    INT      NOT NULL,
        Czas_Zadany_ms   INT      NOT NULL DEFAULT 0,
        Czas_Zakonczenia DATETIME NOT NULL DEFAULT GETDATE()
    );
    CREATE INDEX IX_HC_Wyrob ON dbo.HistoriaCykli(ID_Wyrobu, Czas_Zakonczenia DESC);
END
GO
