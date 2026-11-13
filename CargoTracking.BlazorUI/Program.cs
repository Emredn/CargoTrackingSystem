using CargoTracking.BlazorUI.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Uygulama genelinde tek kopya (singleton): sunucuya giden gRPC kanalı ve il/ilçe/mahalle listesi
builder.Services.AddSingleton<CargoTracking.BlazorUI.Services.CargoApiClient>();
builder.Services.AddSingleton<CargoTracking.BlazorUI.Services.LocationService>();

// Kullanıcı oturumu (sekme) başına bir kopya (scoped): tema, bildirimler, kargo listesi önbelleği
builder.Services.AddScoped<CargoTracking.BlazorUI.Services.UiStateService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
