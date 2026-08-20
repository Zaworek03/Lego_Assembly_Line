using System;

namespace LiniaProdukcyjnaDashboard.Models
{
    public class DigitalTwinStanowiskoVM
    {
        public int IDStanowiska { get; set; }
        public string Nazwa { get; set; }
        public string? Operator { get; set; }
        public string? AktywneZlecenie { get; set; }
        public double? OEE { get; set; }
        public double? FTY { get; set; }
        public int? CzasCykluMs { get; set; }
        public int? CzasPlanowanyMs { get; set; }
        public DateTime? OstatniaCzas { get; set; }
        public string? KodPostoju { get; set; }
        public int SztukDzisiaj { get; set; }
        public int SztukOKDzisiaj => SztukDzisiaj - WadliweDzisiaj;
        public int WadliweDzisiaj { get; set; }

        public bool IsActive => OstatniaCzas.HasValue && (DateTime.Now - OstatniaCzas.Value).TotalMinutes < 15;
        
        public string StatusColor
        {
            get
            {
                if (!string.IsNullOrEmpty(KodPostoju)) return "var(--mud-palette-error)";
                if (!IsActive) return "var(--mud-palette-text-disabled)";
                if (OEE >= 0.85) return "var(--mud-palette-success)";
                if (OEE >= 0.60) return "var(--mud-palette-warning)";
                return "var(--mud-palette-error)";
            }
        }

        public string StatusLabel
        {
            get
            {
                if (!string.IsNullOrEmpty(KodPostoju)) return "Błąd: " + KodPostoju;
                if (!IsActive) return "Bezczynne";
                return "W produkcji";
            }
        }
    }

    public class DigitalTwinSummaryVM
    {
        public int SztukDzisiaj { get; set; }
        public int SztukOKDzisiaj => SztukDzisiaj - WadliweDzisiaj;
        public int WadliweDzisiaj { get; set; }
        public double OEEDzisiaj { get; set; }
        public string? AktywneZlecenie { get; set; }
        public int CykleGodzina { get; set; }
    }
}
