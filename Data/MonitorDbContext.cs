using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PbSqlServerMonitoring.Models;

namespace PbSqlServerMonitoring.Data;

public class MonitorDbContext : IdentityDbContext<ApplicationUser>
{
    public MonitorDbContext(DbContextOptions<MonitorDbContext> options)
        : base(options)
    {
    }

    public DbSet<MetricSnapshotEntity> MetricSnapshots { get; set; } = null!;
    public DbSet<QueryHistoryEntity> QueryHistory { get; set; } = null!;
    public DbSet<BlockingHistoryEntity> BlockingHistory { get; set; } = null!;
    public DbSet<UserPreferenceEntity> UserPreferences { get; set; } = null!;
    public DbSet<ServerConnection> ServerConnections { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // ============================================================
        // MetricSnapshot Indexes
        // ============================================================
        
        // Primary time-based queries
        modelBuilder.Entity<MetricSnapshotEntity>()
            .HasIndex(m => m.Timestamp)
            .HasDatabaseName("IX_MetricSnapshots_Timestamp");
        
        // Composite index for server/database filtering with time range
        // This is the most common query pattern
        modelBuilder.Entity<MetricSnapshotEntity>()
            .HasIndex(m => new { m.ServerName, m.DatabaseName, m.Timestamp })
            .HasDatabaseName("IX_MetricSnapshots_Server_Database_Timestamp");
        
        // Filtered index for blocking queries (only non-zero values)
        modelBuilder.Entity<MetricSnapshotEntity>()
            .HasIndex(m => m.BlockedProcesses)
            .HasFilter("[BlockedProcesses] > 0")
            .HasDatabaseName("IX_MetricSnapshots_BlockedProcesses_Filtered");
            
        // ============================================================
        // QueryHistory Indexes
        // ============================================================
        
        modelBuilder.Entity<QueryHistoryEntity>()
            .HasIndex(q => q.SnapshotId)
            .HasDatabaseName("IX_QueryHistory_SnapshotId");
        
        // Add index on LastExecutionTime for efficient time-range queries
        modelBuilder.Entity<QueryHistoryEntity>()
            .HasIndex(q => q.LastExecutionTime)
            .HasDatabaseName("IX_QueryHistory_LastExecutionTime");
            
        // ============================================================
        // BlockingHistory Indexes
        // ============================================================
            
        modelBuilder.Entity<BlockingHistoryEntity>()
            .HasIndex(b => b.SnapshotId)
            .HasDatabaseName("IX_BlockingHistory_SnapshotId");
            
        // ============================================================
        // Cascade Delete Configuration
        // ============================================================
        
        modelBuilder.Entity<QueryHistoryEntity>()
            .HasOne(q => q.Snapshot)
            .WithMany(s => s.TopQueries)
            .HasForeignKey(q => q.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);
            
        modelBuilder.Entity<BlockingHistoryEntity>()
            .HasOne(b => b.Snapshot)
            .WithMany(s => s.BlockedQueries)
            .HasForeignKey(b => b.SnapshotId)
            .OnDelete(DeleteBehavior.Cascade);
            
        // ============================================================
        // ServerConnection Indexes
        // ============================================================
        
        // Index on UserId for multi-tenant queries
        modelBuilder.Entity<ServerConnection>()
            .HasIndex(c => c.UserId)
            .HasDatabaseName("IX_ServerConnections_UserId");
            
        // Index on Status for filtering enabled/disabled connections
        modelBuilder.Entity<ServerConnection>()
            .HasIndex(c => c.Status)
            .HasDatabaseName("IX_ServerConnections_Status");
            
        // Composite index for user + enabled connections
        modelBuilder.Entity<ServerConnection>()
            .HasIndex(c => new { c.UserId, c.IsEnabled })
            .HasDatabaseName("IX_ServerConnections_UserId_IsEnabled");
    }
}
