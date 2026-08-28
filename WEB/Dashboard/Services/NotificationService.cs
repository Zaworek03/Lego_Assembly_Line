using LiniaProdukcyjnaDashboard.Models;
using Microsoft.Data.SqlClient;

namespace LiniaProdukcyjnaDashboard.Services
{
    /// <summary>
    /// Powiadomienia zapisywane przez Middleware (np. Abort pojedynczej sztuki na stanowisku).
    /// </summary>
    public class NotificationService
    {
        private readonly string _cs;

        public NotificationService(IConfiguration cfg)
        {
            _cs = cfg.GetConnectionString("BazaDanychRB")!;
        }

        public async Task<int> GetUnreadCountAsync()
        {
            const string sql = "SELECT COUNT(*) FROM Powiadomienia WHERE Przeczytane = 0";
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            return Convert.ToInt32(await cmd.ExecuteScalarAsync());
        }

        public async Task<List<Powiadomienie>> GetRecentAsync(int n = 20)
        {
            var sql = $@"
                SELECT TOP {n} ID, Typ, Tresc, ID_Zlecenia, ID_Stanowiska, Utworzono, Przeczytane
                FROM Powiadomienia
                ORDER BY Utworzono DESC";

            var result = new List<Powiadomienie>();
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync())
                result.Add(new Powiadomienie
                {
                    ID           = rdr.GetInt32(0),
                    Typ          = rdr.GetString(1),
                    Tresc        = rdr.GetString(2),
                    IDZlecenia   = rdr.IsDBNull(3) ? null : rdr.GetInt32(3),
                    IDStanowiska = rdr.IsDBNull(4) ? null : rdr.GetInt32(4),
                    Utworzono    = rdr.GetDateTime(5),
                    Przeczytane  = rdr.GetBoolean(6)
                });
            return result;
        }

        public async Task MarkAllReadAsync()
        {
            const string sql = "UPDATE Powiadomienia SET Przeczytane = 1 WHERE Przeczytane = 0";
            await using var conn = new SqlConnection(_cs);
            await conn.OpenAsync();
            await using var cmd = new SqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
