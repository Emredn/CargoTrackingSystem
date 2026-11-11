using CargoTracking.Server.Repositories;
using CargoTracking.Server.Services;

var builder = WebApplication.CreateBuilder(args);

// gRPC ve Repository servislerini Dependency Injection'a ekliyoruz
builder.Services.AddGrpc();
builder.Services.AddSingleton<CargoRepository>();

builder.Services.AddSingleton<GeoRoutingService>();
builder.Services.AddSingleton<DeliverySimulationManager>();

var app = builder.Build();

// Yazdığımız gRPC servisini dış dünyaya açıyoruz
app.MapGrpcService<CargoGrpcService>();

app.MapGet("/", () => "Bu bir gRPC sunucusudur. Bağlanmak için bir gRPC Client kullanın.");

app.Run();