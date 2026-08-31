using LiniaProdukcyjnaDashboard.Components;
using LiniaProdukcyjnaDashboard.Services;
using MudBlazor.Services;

// ── Tylko jedna instancja naraz ─────────────────────────────────────────
// Dwie kopie aplikacji bija sie o instancje LocalDB ("SQL Server process failed
// to start") i wtedy ZADNA nie widzi bazy. Blokada nazwanym mutexem eliminuje to
// u zrodla - druga kopia konczy sie od razu z czytelnym komunikatem.
using var jedynaInstancja = new Mutex(true, @"Global\LiniaMontazowa_Dashboard", out bool pierwszy);
if (!pierwszy)
{
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.WriteLine("Dashboard juz dziala (inna instancja). Uruchamiam tylko jedna kopie.");
    Console.WriteLine("Aby zrestartowac, uzyj skrotu 'Zatrzymaj Linia Montazowa', potem 'Uruchom Linia Montazowa'.");
    Console.ResetColor();
    return;
}

// ContentRoot przypiety do katalogu z plikiem .exe, a nie do biezacego katalogu
// procesu. Bez tego uruchomienie aplikacji z innego miejsca (dwuklik w bin\,
// skrot, stary skrypt startowy ustawiajacy WorkingDirectory na bin\) sprawialo,
// ze manifest zasobow statycznych nie mial jak sie odnalezc: /app.css i /app.js
// odpowiadaly 404, strona ladowala sie bez wlasnych stylow i skryptow i wygladala
// jak stara wersja sprzed calego frontendu.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args            = args,
    ContentRootPath = AppContext.BaseDirectory
});

// Aplikacja startuje jako goly .exe z katalogu bin, czyli w srodowisku Production -
// a tam ASP.NET domyslnie NIE laduje manifestu static web assets. Efekt byl taki, ze
// /app.css i /app.js odpowiadaly HTTP 200 z pustym cialem (0 B), wiec wlasne style
// i skrypty z wwwroot nigdy sie nie wykonywaly. Manifest lezy obok exe - ladujemy go
// jawnie, niezaleznie od srodowiska.
builder.WebHost.UseStaticWebAssets();

// Bez tego uruchomienie .exe bezposrednio (dwuklik w bin\) wstaje na domyslnym
// porcie 5000 i tworzy druga, konkurencyjna instancje. Skrypt startowy nadal
// moze nadpisac to argumentem --urls.
if (string.IsNullOrEmpty(builder.Configuration["urls"]) &&
    string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")))
{
    builder.WebHost.UseUrls("http://localhost:5200");
}

// Umożliwia działanie jako usługa Windows (działa też normalnie z konsoli/VS)
builder.Host.UseWindowsService();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// MudBlazor
builder.Services.AddMudServices();

// Serwisy aplikacji
builder.Services.AddScoped<ProductionDataService>();
builder.Services.AddSingleton<AuthService>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddSingleton<ThemeService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
