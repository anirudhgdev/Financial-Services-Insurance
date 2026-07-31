using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ClaimSettlement.Infrastructure.Persistence.DesignTime;

public sealed class ClaimSettlementDbContextFactory : IDesignTimeDbContextFactory<ClaimSettlementDbContext>
{
    public ClaimSettlementDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ClaimSettlementDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=ClaimSettlementDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True");

        return new ClaimSettlementDbContext(optionsBuilder.Options);
    }
}
