-- Uruchom ten skrypt RAZ po uruchomieniu CreateDatabase.sql
-- Dodaje kolumny logowania do tabeli Operator

USE [BazaDanychRB];
GO

-- Dodaj kolumny logowania (jesli nie istnieja)
IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Operator]') AND name = N'Login')
    ALTER TABLE [dbo].[Operator] ADD Login NVARCHAR(50) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Operator]') AND name = N'Haslo')
    ALTER TABLE [dbo].[Operator] ADD Haslo NVARCHAR(100) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(N'[dbo].[Operator]') AND name = N'Rola')
    ALTER TABLE [dbo].[Operator] ADD Rola NVARCHAR(20) DEFAULT 'Operator' NULL;
GO

-- Konto supervisora (zmien haslo przed produkcja!)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Operator] WHERE Login = 'supervisor')
BEGIN
    INSERT INTO [dbo].[Operator] (Imie_Nazwisko, Stawka_Godzinowa, Poziom_Zaawansowania, Login, Haslo, Rola)
    VALUES ('Supervisor', 0, 'Supervisor', 'supervisor', 'admin123', 'Supervisor');
END
GO

-- Przykladowy operator (zmien dane i haslo)
IF NOT EXISTS (SELECT 1 FROM [dbo].[Operator] WHERE Login = 'jan.kowalski')
BEGIN
    INSERT INTO [dbo].[Operator] (Imie_Nazwisko, Stawka_Godzinowa, Poziom_Zaawansowania, Login, Haslo, Rola)
    VALUES ('Jan Kowalski', 35.00, 'Senior', 'jan.kowalski', 'operator123', 'Operator');
END
GO

-- Przykladowy wyrob
IF NOT EXISTS (SELECT 1 FROM [dbo].[Wyrob] WHERE Nazwa_Wyrobu = 'LEGO Set A')
BEGIN
    INSERT INTO [dbo].[Wyrob] (Nazwa_Wyrobu) VALUES ('LEGO Set A');
END
GO

-- Przykladowe zlecenie
IF NOT EXISTS (SELECT 1 FROM [dbo].[Zlecenie_Produkcyjne] WHERE Nazwa_Zlecenia = 'ZL-001')
BEGIN
    INSERT INTO [dbo].[Zlecenie_Produkcyjne] (Nazwa_Zlecenia, Ilosc_Sztuk, ID_Wyrobu, Czas_Planowany_ms, Status_Zlecenia)
    VALUES ('ZL-001', 100, 1, 3500, 'W toku');
END
GO

PRINT 'Gotowe! Konta i dane przykladowe dodane.';
GO
