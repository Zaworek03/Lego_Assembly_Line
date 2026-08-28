using LiniaProdukcyjnaDashboard.Models;

namespace LiniaProdukcyjnaDashboard.Services
{
    /// <summary>Stan sesji aplikacji (bez weryfikacji uzytkownikow).</summary>
    public class AuthService
    {
        private AppUser? _currentUser;

        public AppUser? CurrentUser => _currentUser;
        public bool IsLoggedIn => _currentUser != null;

        public event Action? OnChange;

        /// <summary>
        /// Wejscie do aplikacji bez logowania - system nie sledzi juz pojedynczych
        /// uzytkownikow, wiec nie ma czego weryfikowac w bazie.
        /// </summary>
        public void Login()
        {
            _currentUser = new AppUser
            {
                IDOperatora  = 1,
                ImieNazwisko = "Supervisor",
                Rola         = "Supervisor"
            };
            OnChange?.Invoke();
        }

        public void Logout()
        {
            _currentUser = null;
            OnChange?.Invoke();
        }
    }
}
