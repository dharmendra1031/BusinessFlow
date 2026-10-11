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
            new LeadStatus { Id = 6, Name = "Lost", SortOrder = 6 });

        modelBuilder.Entity<LeadAssignment>().HasIndex(x => x.LeadId);
        modelBuilder.Entity<LeadAssignment>().HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<LeadStatusChange>().HasIndex(x => x.LeadId);
        modelBuilder.Entity<LeadStatusChange>().HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId)
            .OnDelete(DeleteBehavior.Cascade);
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
