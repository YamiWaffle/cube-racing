using CubeRacing.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CubeRacing.Tests.Helpers;

public static class TestDbContextFactory
{
    public static AppDbContext Create()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(opts);
    }
}
