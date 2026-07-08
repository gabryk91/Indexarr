using Indexarr.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Indexarr.Web.Data;

public sealed class IndexarrDbContext : DbContext
{
    public IndexarrDbContext(DbContextOptions<IndexarrDbContext> options)
        : base(options)
    {
    }

    public DbSet<AppConfigurationEntity> AppConfigurations => Set<AppConfigurationEntity>();

    public DbSet<AdminUserEntity> AdminUsers => Set<AdminUserEntity>();

    public DbSet<IndexerHealthCheckEntity> IndexerHealthChecks => Set<IndexerHealthCheckEntity>();

    public DbSet<IndexerStateEntity> IndexerStates => Set<IndexerStateEntity>();

    public DbSet<BlockedIndexerEntity> BlockedIndexers => Set<BlockedIndexerEntity>();

    public DbSet<AutoAddFailedCandidateEntity> AutoAddFailedCandidates => Set<AutoAddFailedCandidateEntity>();

    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppConfigurationEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Language).HasMaxLength(8);
            entity.Property(x => x.Timezone).HasMaxLength(128);
            entity.Property(x => x.ServiceMode).HasMaxLength(32);
            entity.Property(x => x.ProwlarrUrl).HasMaxLength(1024);
            entity.Property(x => x.ProwlarrApiKey).HasMaxLength(512);
            entity.Property(x => x.AutoAddProtocolFilter).HasMaxLength(64);
            entity.Property(x => x.AutoAddLanguageFilter).HasMaxLength(128);
            entity.Property(x => x.AutoAddLanguageMatchMode).HasMaxLength(16);
            entity.Property(x => x.AutoAddPrivacyFilter).HasMaxLength(64);
            entity.Property(x => x.AutoAddDefaultLimitsUnit).HasMaxLength(32);
            entity.Property(x => x.AutoAddDefaultPreferMagnetUrlMode).HasMaxLength(16);
            entity.Property(x => x.AutoAddDefaultDownloadClient).HasMaxLength(128);
            entity.Property(x => x.AutoAddDefaultFilterByUploader).HasMaxLength(256);
            entity.Property(x => x.AutoAddDefaultTags).HasMaxLength(512);
        });

        modelBuilder.Entity<AdminUserEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Username).HasMaxLength(64);
            entity.Property(x => x.PasswordHash).HasMaxLength(255);
            entity.Property(x => x.Role).HasMaxLength(32);
            entity.HasIndex(x => x.Username).IsUnique();
        });

        modelBuilder.Entity<IndexerHealthCheckEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(256);
            entity.Property(x => x.Protocol).HasMaxLength(64);
            entity.Property(x => x.Implementation).HasMaxLength(128);
            entity.Property(x => x.Result).HasMaxLength(32);
            entity.HasIndex(x => new { x.IndexerId, x.CheckedAtUtc });
        });

        modelBuilder.Entity<IndexerStateEntity>(entity =>
        {
            entity.HasKey(x => x.IndexerId);
            entity.Property(x => x.Name).HasMaxLength(256);
            entity.Property(x => x.Protocol).HasMaxLength(64);
            entity.Property(x => x.Implementation).HasMaxLength(128);
            entity.Property(x => x.LastResult).HasMaxLength(32);
        });

        modelBuilder.Entity<BlockedIndexerEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(256);
            entity.Property(x => x.DefinitionName).HasMaxLength(256);
            entity.Property(x => x.Protocol).HasMaxLength(64);
            entity.Property(x => x.Implementation).HasMaxLength(128);
            entity.HasIndex(x => x.OriginalIndexerId);
            entity.HasIndex(x => x.Name);
            entity.HasIndex(x => x.DefinitionName);
        });

        modelBuilder.Entity<AutoAddFailedCandidateEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(256);
            entity.Property(x => x.DefinitionName).HasMaxLength(256);
            entity.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<AuditLogEntity>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.IndexerName).HasMaxLength(256);
            entity.Property(x => x.Action).HasMaxLength(64);
            entity.Property(x => x.Mode).HasMaxLength(32);
            entity.HasIndex(x => x.CreatedAtUtc);
        });
    }
}
