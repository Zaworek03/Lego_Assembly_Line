using LiniaProdukcyjnaDashboard.Models;
using Microsoft.Data.SqlClient;

namespace LiniaProdukcyjnaDashboard.Services
{
    /// <summary>Prosty serwis logowania — sprawdza Login/Haslo w tabeli Operator.</summary>
    public class AuthService
    {
        private readonly string _cs;
        private AppUser? _currentUser;

        public AppUser? CurrentUser => _currentUser;
        public bool IsLoggedIn => _currentUser != null;

        public event Action? OnChange;

        public AuthService(IConfiguration cfg)
        {
            _cs = cfg.GetConnectionString("BazaDanychRB")!;
        }

        public async Task<bool> LoginAsync(string login, string haslo)
        {
            const string sql = @"
                SELECT ID_Operatora, Imie_Nazwisko, ISNULL(Rola, 'Operator')
                FROM [dbo].[Operator]
                WHERE LOWER(LTRIM(RTRIM(Login))) = LOWER(LTRIM(RTRIM(@Login)))
                  AND Haslo = @Haslo";

            try
            {
                await using var conn = new SqlConnection(_cs);
                await conn.OpenAsync();
                await using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@Login", login);
                cmd.Parameters.AddWithValue("@Haslo", haslo);
                await using var rdr = await cmd.ExecuteReaderAsync();

                if (!await rdr.ReadAsync()) return false;

                _currentUser = new AppUser
                {
                    IDOperatora  = rdr.GetInt32(0),
                    ImieNazwisko = rdr.GetString(1),
                    Rola         = rdr.GetString(2)
                };
                OnChange?.Invoke();
                return true;
            }
            catch (Exception ex) 
            { 
                Console.WriteLine($"SQL Login Error: {ex.Message}");
                return false; 
            }
        }

        public void Logout()
        {
            _currentUser = null;
            OnChange?.Invoke();
        }
    }
}
