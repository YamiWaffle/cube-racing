using CubeRacing.API.Middleware;
using CubeRacing.Application.Config;
using CubeRacing.Application.Interfaces;
using CubeRacing.Application.UseCases;
using CubeRacing.Domain.Interfaces;
using CubeRacing.Infrastructure.Persistence;
using CubeRacing.Infrastructure.Persistence.Repositories;
using CubeRacing.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        "Server=localhost,1433;Database=CubeRacing;User=sa;Password=CubeRacing!123;TrustServerCertificate=True"));

// Register Configuration
builder.Services.Configure<GameSettings>(builder.Configuration.GetSection("GameSettings"));

// Register Repositories
builder.Services.AddScoped<IPlayerRepository, PlayerRepository>();
builder.Services.AddScoped<IGameSessionRepository, GameSessionRepository>();
builder.Services.AddScoped<IBetRepository, BetRepository>();

// Register Current Session Store
builder.Services.AddSingleton<ICurrentSessionStore, CurrentSessionStore>();

// Register Use Cases
builder.Services.AddScoped<CreatePlayer>();
builder.Services.AddScoped<GetCurrentSession>();
builder.Services.AddScoped<PlaceBet>();
builder.Services.AddScoped<GetLeaderboard>();

// Register Game Hub Notifier
builder.Services.AddSingleton<IGameHubNotifier, GameHubNotifier>();

// Add Controllers
builder.Services.AddControllers();

var app = builder.Build();

app.UseHttpsRedirection();

// Add Token Auth Middleware
app.UseMiddleware<TokenAuthMiddleware>();

app.MapControllers();

app.Run();
