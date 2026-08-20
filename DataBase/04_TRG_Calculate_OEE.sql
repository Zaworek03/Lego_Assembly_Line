USE [BazaDanychRB];
GO

CREATE OR ALTER TRIGGER [dbo].[TRG_Realizacja_OEE]
ON [dbo].[Realizacja_Produkcji]
AFTER INSERT
AS
BEGIN
    SET NOCOUNT ON;

    INSERT INTO [dbo].[Wskazniki] (
        ID_Zlecenia, ID_Realizacji, ID_Stanowiska, 
        Wydajnosc, Dostepnosc, Jakosc, Wskaznik_OEE, 
        Czas_Realizacji_ms, Wydajnosc_Pracy_Operatora, 
        Czas_Cyklu_ms, Wskaznik_FTY, DataCzas_Pomiaru
    )
    SELECT 
        i.ID_Zlecenia,
        i.ID,
        i.ID_Stanowiska,
        
        CASE WHEN i.Czas_Cyklu_ms <= 0 THEN 1.0 
             ELSE (CAST(z.Czas_Planowany_ms AS DECIMAL(18,4)) * i.Ilosc_Wyprodukowanych) / i.Czas_Cyklu_ms 
        END AS Wydajnosc,

        CASE WHEN i.Czas_Splywu_ms <= 0 THEN 1.0
             ELSE CAST(i.Czas_Cyklu_ms AS DECIMAL(18,4)) / i.Czas_Splywu_ms
        END AS Dostepnosc,

        CASE WHEN i.Ilosc_Wyprodukowanych <= 0 THEN 0.0
             ELSE CAST((i.Ilosc_Wyprodukowanych - i.Liczba_Wadliwych) AS DECIMAL(18,4)) / i.Ilosc_Wyprodukowanych
        END AS Jakosc,

        (
            CASE WHEN i.Czas_Cyklu_ms <= 0 THEN 1.0 ELSE (CAST(z.Czas_Planowany_ms AS DECIMAL(18,4)) * i.Ilosc_Wyprodukowanych) / i.Czas_Cyklu_ms END
            * 
            CASE WHEN i.Czas_Splywu_ms <= 0 THEN 1.0 ELSE CAST(i.Czas_Cyklu_ms AS DECIMAL(18,4)) / i.Czas_Splywu_ms END
            * 
            CASE WHEN i.Ilosc_Wyprodukowanych <= 0 THEN 0.0 ELSE CAST((i.Ilosc_Wyprodukowanych - i.Liczba_Wadliwych) AS DECIMAL(18,4)) / i.Ilosc_Wyprodukowanych END
        ) AS Wskaznik_OEE,

        i.Czas_Splywu_ms AS Czas_Realizacji_ms,
        
        CASE WHEN i.Czas_Splywu_ms <= 0 THEN 0
             ELSE (CAST(i.Ilosc_Wyprodukowanych AS DECIMAL(18,4)) / (CAST(i.Czas_Splywu_ms AS DECIMAL(18,4)) / 3600000.0))
        END AS Wydajnosc_Pracy_Operatora,

        i.Czas_Cyklu_ms,

        CASE WHEN i.Ilosc_Wyprodukowanych <= 0 THEN 0.0
             ELSE CAST((i.Ilosc_Wyprodukowanych - i.Liczba_Wadliwych) AS DECIMAL(18,4)) / i.Ilosc_Wyprodukowanych
        END AS Wskaznik_FTY,

        i.Czas_Zakonczenia
    FROM inserted i
    JOIN dbo.Zlecenie_Produkcyjne z ON z.ID_Zlecenia = i.ID_Zlecenia;
END;
GO