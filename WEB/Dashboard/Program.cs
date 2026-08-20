using LiniaProdukcyjnaDashboard.Components;
using LiniaProdukcyjnaDashboard.Services;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddHostedService<ProductionSimulatorService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Error", createScopeForErrors: true);

app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
