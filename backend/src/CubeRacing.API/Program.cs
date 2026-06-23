using CubeRacing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        "Server=localhost,1433;Database=CubeRacing;User=sa;Password=CubeRacing!123;TrustServerCertificate=True"));

var app = builder.Build();

app.UseHttpsRedirection();

app.Run();
