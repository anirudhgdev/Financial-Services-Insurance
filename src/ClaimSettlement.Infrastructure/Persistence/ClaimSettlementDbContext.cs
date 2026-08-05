using ClaimSettlement.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ClaimSettlement.Infrastructure.Persistence;

public sealed class ClaimSettlementDbContext : DbContext
{
    public ClaimSettlementDbContext(DbContextOptions<ClaimSettlementDbContext> options)
        : base(options)
    {
    }

    public DbSet<Claim> Claims => Set<Claim>();

    public DbSet<ClaimPipelineState> ClaimPipelineStates => Set<ClaimPipelineState>();

    public DbSet<AgentOutput> AgentOutputs => Set<AgentOutput>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<ProviderConfiguration> ProviderConfigurations => Set<ProviderConfiguration>();

    public DbSet<AdjusterAssignment> AdjusterAssignments => Set<AdjusterAssignment>();

    public override int SaveChanges()
    {
        EnforceAuditLogAppendOnly();
        return base.SaveChanges();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceAuditLogAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnforceAuditLogAppendOnly();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceAuditLogAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Claim>(entity =>
        {
            entity.HasKey(x => x.ClaimId);
            entity.Property(x => x.ProviderId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PolicyNumber).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ClaimantId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ClaimType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(64).IsRequired();
            entity.Property(x => x.LossAmount).HasPrecision(18, 2);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasIndex(x => new { x.ProviderId, x.Status, x.CreatedAt });
            entity.HasIndex(x => new { x.ProviderId, x.PolicyNumber, x.DateOfLoss });
        });

        modelBuilder.Entity<ClaimPipelineState>(entity =>
        {
            entity.HasKey(x => x.ClaimId);
            entity.Property(x => x.ProviderId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.CurrentStep).HasMaxLength(64).IsRequired();
            entity.Property(x => x.CompletedSteps).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.AgentOutputs).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.ProviderConfigSnapshot).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.Status).HasMaxLength(64).IsRequired();
            entity.Property(x => x.StartedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasOne(x => x.Claim)
                .WithOne(x => x.PipelineState)
                .HasForeignKey<ClaimPipelineState>(x => x.ClaimId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_ClaimPipelineState_CompletedSteps_IsJson", "ISJSON([CompletedSteps]) = 1");
                table.HasCheckConstraint("CK_ClaimPipelineState_AgentOutputs_IsJson", "ISJSON([AgentOutputs]) = 1");
                table.HasCheckConstraint("CK_ClaimPipelineState_ProviderConfigSnapshot_IsJson", "ISJSON([ProviderConfigSnapshot]) = 1");
            });

            entity.HasIndex(x => new { x.ProviderId, x.Status });
        });

        modelBuilder.Entity<AgentOutput>(entity =>
        {
            entity.HasKey(x => x.OutputId);
            entity.Property(x => x.AgentId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.OutputPayload).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.SchemaVersion).HasMaxLength(32).IsRequired();
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasOne(x => x.Claim)
                .WithMany(x => x.AgentOutputs)
                .HasForeignKey(x => x.ClaimId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AgentOutput_OutputPayload_IsJson", "ISJSON([OutputPayload]) = 1");
            });

            entity.HasIndex(x => new { x.ClaimId, x.AgentId, x.CreatedAt });
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(x => x.EntryId);
            entity.Property(x => x.ProviderId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.EventType).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ActorId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ActorType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.Payload).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.Timestamp).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasOne(x => x.Claim)
                .WithMany(x => x.AuditLogs)
                .HasForeignKey(x => x.ClaimId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_AuditLog_Payload_IsJson", "ISJSON([Payload]) = 1");
            });

            entity.HasIndex(x => new { x.ProviderId, x.Timestamp });
            entity.HasIndex(x => new { x.ClaimId, x.Timestamp });
        });

        modelBuilder.Entity<ProviderConfiguration>(entity =>
        {
            entity.HasKey(x => x.ProviderId);
            entity.Property(x => x.ProviderId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.ProviderName).HasMaxLength(256).IsRequired();
            entity.Property(x => x.ManualReviewFraudThreshold).HasPrecision(4, 2);
            entity.Property(x => x.ManualReviewClaimAmountThreshold).HasPrecision(18, 2);
            entity.Property(x => x.SupportedClaimTypes).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.SupportedNotificationChannels).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.ClaimTypeMandatoryFields).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.CoverageMappingRules).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.ExclusionSets).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.AlwaysManualClaimTypes).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(x => x.IsActive).HasDefaultValue(true);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_ProviderConfiguration_FraudThreshold_Range", "[ManualReviewFraudThreshold] >= 0.30 AND [ManualReviewFraudThreshold] <= 0.90");
                table.HasCheckConstraint("CK_ProviderConfiguration_SupportedClaimTypes_IsJson", "ISJSON([SupportedClaimTypes]) = 1");
                table.HasCheckConstraint("CK_ProviderConfiguration_SupportedNotificationChannels_IsJson", "ISJSON([SupportedNotificationChannels]) = 1");
                table.HasCheckConstraint("CK_ProviderConfiguration_ClaimTypeMandatoryFields_IsJson", "ISJSON([ClaimTypeMandatoryFields]) = 1");
                table.HasCheckConstraint("CK_ProviderConfiguration_CoverageMappingRules_IsJson", "ISJSON([CoverageMappingRules]) = 1");
                table.HasCheckConstraint("CK_ProviderConfiguration_ExclusionSets_IsJson", "ISJSON([ExclusionSets]) = 1");
                table.HasCheckConstraint("CK_ProviderConfiguration_AlwaysManualClaimTypes_IsJson", "ISJSON([AlwaysManualClaimTypes]) = 1");
            });

            entity.HasIndex(x => x.IsActive);
        });

        modelBuilder.Entity<AdjusterAssignment>(entity =>
        {
            entity.HasKey(x => x.AssignmentId);
            entity.Property(x => x.ProviderId).HasMaxLength(64).IsRequired();
            entity.Property(x => x.AdjusterId).HasMaxLength(128).IsRequired();
            entity.Property(x => x.Decision).HasMaxLength(32);
            entity.Property(x => x.Rationale).HasMaxLength(4000);
            entity.Property(x => x.SettlementOverride).HasPrecision(18, 2);
            entity.Property(x => x.AssignedAt).HasDefaultValueSql("SYSUTCDATETIME()");

            entity.HasOne(x => x.Claim)
                .WithMany(x => x.AdjusterAssignments)
                .HasForeignKey(x => x.ClaimId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(x => new { x.ProviderId, x.AssignedAt });
            entity.HasIndex(x => new { x.AdjusterId, x.AssignedAt });
            entity.HasIndex(x => new { x.ClaimId, x.AssignedAt });
        });
    }

    private void EnforceAuditLogAppendOnly()
    {
        IEnumerable<EntityEntry<AuditLog>> changedAuditLogs = ChangeTracker
            .Entries<AuditLog>()
            .Where(entry => entry.State is EntityState.Modified or EntityState.Deleted);

        if (changedAuditLogs.Any())
        {
            throw new InvalidOperationException("AuditLog is append-only. Update/delete operations are not allowed.");
        }
    }
}
