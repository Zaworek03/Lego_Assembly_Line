namespace LiniaProdukcyjnaDashboard.Services
{
    /// <summary>Globalny stan wyboru jasny/ciemny motyw, dzielony przez cala aplikacje.</summary>
    public class ThemeService
    {
        public bool IsDarkMode { get; private set; } = true;
        public event Action? OnChange;

        public void SetDarkMode(bool value)
        {
            if (IsDarkMode == value) return;
            IsDarkMode = value;
            OnChange?.Invoke();
        }

        public void Toggle() => SetDarkMode(!IsDarkMode);
    }
}
