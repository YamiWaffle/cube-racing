// src/CubeRacing.API/Program.cs
using CubeRacing.Application.Config;
using CubeRacing.Application.GameEngine;
using CubeRacing.Application.Interfaces;
using CubeRacing.Application.UseCases;
using CubeRacing.Domain.Interfaces;
using CubeRacing.Infrastructure.Hubs;
using CubeRacing.Infrastructure.Messaging;
using CubeRacing.Infrastructure.Messaging.Consumers;
using CubeRacing.Infrastructure.Persistence;
using CubeRacing.Infrastructure.Persistence.Repositories;
using CubeRacing.Infrastructure.Services;
using CubeRacing.API.Middleware;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// Config
builder.Services.Configure<GameSettings>(builder.Configuration.GetSection("GameSettings"));
builder.Services.Configure<List<NpcConfig>>(builder.Configuration.GetSection("Npcs"));

// EF Core
builder.Services.AddDbContext<AppDbContext>(opts =>
    opts.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Repositories
builder.Services.AddScoped<IPlayerRepository, PlayerRepository>();
builder.Services.AddScoped<IGameSessionRepository, GameSessionRepository>();
builder.Services.AddScoped<IBetRepository, BetRepository>();
builder.Services.AddScoped<IGameRoundRepository, GameRoundRepository>();

// RabbitMQ
builder.Services.AddSingleton<IConnectionFactory>(_ =>
    new ConnectionFactory
    {
        HostName = builder.Configuration["RabbitMQ:Host"] ?? "localhost",
        Port = int.Parse(builder.Configuration["RabbitMQ:Port"] ?? "5672"),
        UserName = builder.Configuration["RabbitMQ:Username"] ?? "guest",
        Password = builder.Configuration["RabbitMQ:Password"] ?? "guest",
        DispatchConsumersAsync = true
    });
builder.Services.AddSingleton<IMessagePublisher, RabbitMqPublisher>();

// SignalR
builder.Services.AddSignalR();
builder.Services.AddSingleton<IGameHubNotifier, GameHubNotifier>();

// Application singletons
builder.Services.AddSingleton<ICurrentSessionStore, CurrentSessionStore>();
builder.Services.AddSingleton<ISessionCompletionSignal, SessionCompletionSignal>();

// Application use cases (scoped)
builder.Services.AddScoped<CreatePlayer>();
builder.Services.AddScoped<PlaceBet>();
builder.Services.AddScoped<GetCurrentSession>();
builder.Services.AddScoped<GetLeaderboard>();
builder.Services.AddScoped<SettleSession>();
builder.Services.AddScoped<SettlementCalculator>();

// Background services
builder.Services.AddHostedService<GameSessionManager>();
builder.Services.AddHostedService<BettingEndedConsumer>();
builder.Services.AddHostedService<RoundExecutedConsumer>();
builder.Services.AddHostedService<RaceCompletedConsumer>();
builder.Services.AddHostedService<SettlementDoneConsumer>();

// Web API
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()));

var app = builder.Build();

// Auto-migrate on startup
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
}

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseMiddleware<TokenAuthMiddleware>();
app.MapControllers();
app.MapHub<GameHub>("/hubs/game");

app.Run();
