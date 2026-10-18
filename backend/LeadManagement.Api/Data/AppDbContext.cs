using LeadManagement.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LeadManagement.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<long>, long>(options)
{
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<LeadSource> LeadSources => Set<LeadSource>();
    public DbSet<LeadStatus> LeadStatuses => Set<LeadStatus>();
    public DbSet<LeadAssignment> LeadAssignments => Set<LeadAssignment>();
    public DbSet<LeadStatusChange> LeadStatusChanges => Set<LeadStatusChange>();
    public DbSet<LeadFollowUp> LeadFollowUps => Set<LeadFollowUp>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DevelopmentProject> DevelopmentProjects => Set<DevelopmentProject>();
    public DbSet<DevelopmentTask> DevelopmentTasks => Set<DevelopmentTask>();
    public DbSet<DevelopmentProject> DevelopmentProjects => Set<DevelopmentProject>();
    public DbSet<DevelopmentTask> DevelopmentTasks => Set<DevelopmentTask>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ApplicationUser>()
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(u => u.ManagerId)
            .OnDelete(DeleteBehavior.NoAction);

        modelBuilder.Entity<Lead>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(256);
            entity.Property(x => x.CompanyName).HasMaxLength(200);
            entity.Property(x => x.CustomerCode).HasMaxLength(30);
            entity.Property(x => x.Address).HasMaxLength(500);
            entity.Property(x => x.LeadGivenBy).HasMaxLength(150);
            entity.Property(x => x.ProjectAmount).HasPrecision(18, 3);
            entity.HasIndex(x => x.CustomerCode).IsUnique().HasFilter("[CustomerCode] IS NOT NULL");
            entity.HasIndex(x => x.LeadGivenBy);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ModifiedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.Property(x => x.Requirement).HasMaxLength(2000);
            entity.HasIndex(x => new { x.AssignedToId, x.StatusId, x.CreatedAtUtc });
            entity.HasIndex(x => x.Phone);
            entity.HasOne(x => x.AssignedTo).WithMany().HasForeignKey(x => x.AssignedToId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedById)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Source).WithMany().HasForeignKey(x => x.SourceId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Status).WithMany().HasForeignKey(x => x.StatusId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<DevelopmentProject>(entity =>
        {
            entity.Property(p => p.Name).HasMaxLength(150).IsRequired();
            entity.Property(p => p.Description).HasMaxLength(2000);
            entity.Property(p => p.Status).HasMaxLength(20).IsRequired();
            entity.HasIndex(p => new { p.OwnerId, p.Status });
            entity.HasIndex(p => p.LeadId);
            entity.HasOne(p => p.Lead).WithMany().HasForeignKey(p => p.LeadId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(p => p.Owner).WithMany().HasForeignKey(p => p.OwnerId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(p => p.CreatedById)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<DevelopmentTask>(entity =>
        {
            entity.Property(t => t.Title).HasMaxLength(150).IsRequired();
            entity.Property(t => t.Description).HasMaxLength(2000);
            entity.Property(t => t.Status).HasMaxLength(20).IsRequired();
            entity.HasIndex(t => new { t.ProjectId, t.Status });
            entity.HasIndex(t => t.AssignedToId);
            entity.HasOne(t => t.Project).WithMany(p => p.Tasks).HasForeignKey(t => t.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(t => t.AssignedTo).WithMany().HasForeignKey(t => t.AssignedToId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(t => t.CreatedById)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<LeadSource>().Property(x => x.Name).HasMaxLength(60);
        modelBuilder.Entity<LeadStatus>().Property(x => x.Name).HasMaxLength(60);
        modelBuilder.Entity<LeadSource>().HasData(
            new LeadSource { Id = 1, Name = "Manual" },
            new LeadSource { Id = 2, Name = "Website" },
            new LeadSource { Id = 3, Name = "Facebook" },
            new LeadSource { Id = 4, Name = "WhatsApp" });
        modelBuilder.Entity<LeadStatus>().HasData(
            new LeadStatus { Id = 1, Name = "New", SortOrder = 1 },
            new LeadStatus { Id = 2, Name = "Contacted", SortOrder = 2 },
            new LeadStatus { Id = 3, Name = "Qualified", SortOrder = 3 },
            new LeadStatus { Id = 4, Name = "Proposal", SortOrder = 4 },
            new LeadStatus { Id = 5, Name = "Won", SortOrder = 5 },
            new LeadStatus { Id = 6, Name = "Lost", SortOrder = 6 },
            new LeadStatus { Id = 7, Name = "Active", SortOrder = 7 },
            new LeadStatus { Id = 8, Name = "Closed", SortOrder = 8 },
            new LeadStatus { Id = 9, Name = "Not Interested", SortOrder = 9 });

        modelBuilder.Entity<LeadAssignment>().HasIndex(x => x.LeadId);
        modelBuilder.Entity<LeadAssignment>().HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<LeadStatusChange>().HasIndex(x => x.LeadId);
        modelBuilder.Entity<LeadStatusChange>().HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.Property(x => x.Action).HasMaxLength(50);
            entity.Property(x => x.TableName).HasMaxLength(60);
            entity.Property(x => x.ChangedFields).HasMaxLength(1000);
            entity.HasIndex(x => new { x.TableName, x.RecordId });
        });
        modelBuilder.Entity<DevelopmentProject>(entity =>
        {
            entity.Property(x => x.Title).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.HasIndex(x => x.LeadId).IsUnique();
            entity.HasIndex(x => new { x.OwnerId, x.Status });
            entity.HasOne(x => x.Lead).WithMany().HasForeignKey(x => x.LeadId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedById)
                .OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<DevelopmentTask>(entity =>
        {
            entity.Property(x => x.Title).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Priority).HasMaxLength(10).IsRequired();
            entity.HasIndex(x => new { x.ProjectId, x.Status });
            entity.HasIndex(x => new { x.AssignedToId, x.DueDate });
            entity.HasOne(x => x.Project).WithMany(x => x.Tasks).HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.AssignedTo).WithMany().HasForeignKey(x => x.AssignedToId)
                .OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<LeadFollowUp>(entity =>
        {
            entity.Property(x => x.Notes).HasMaxLength(2000);
            entity.HasIndex(x => new { x.AssignedToId, x.DueAtUtc });
            entity.HasOne(x => x.Lead).WithMany().HasForeignKey(x => x.LeadId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AssignedToId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedById)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
