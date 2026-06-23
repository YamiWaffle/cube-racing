using CubeRacing.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CubeRacing.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Player> Players => Set<Player>();
    public DbSet<GameSession> GameSessions => Set<GameSession>();
    public DbSet<Bet> Bets => Set<Bet>();
    public DbSet<GameRound> GameRounds => Set<GameRound>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Player>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Nickname).HasMaxLength(50).IsRequired();
            e.HasIndex(p => p.Token).IsUnique();
        });

        mb.Entity<GameSession>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Status).HasConversion<string>();
        });

        mb.Entity<Bet>(e =>
        {
            e.HasKey(b => b.Id);
            e.HasIndex(b => new { b.PlayerId, b.SessionId }).IsUnique();
        });

        mb.Entity<GameRound>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.MovementDataJson).HasColumnType("nvarchar(max)");
        });
    }
}
