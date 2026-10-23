using BusinessFlow.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BusinessFlow.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<long>, long>(options)
{
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<LeadStatusApproval> LeadStatusApprovals => Set<LeadStatusApproval>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<RolePermissionGrant> RolePermissionGrants => Set<RolePermissionGrant>();
    public DbSet<UserPermissionRestriction> UserPermissionRestrictions => Set<UserPermissionRestriction>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMembership> TeamMemberships => Set<TeamMembership>();
    public DbSet<LeadSource> LeadSources => Set<LeadSource>();
    public DbSet<LeadStatus> LeadStatuses => Set<LeadStatus>();
    public DbSet<LeadAssignment> LeadAssignments => Set<LeadAssignment>();
    public DbSet<LeadStatusChange> LeadStatusChanges => Set<LeadStatusChange>();
    public DbSet<LeadFollowUp> LeadFollowUps => Set<LeadFollowUp>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<DevelopmentProject> DevelopmentProjects => Set<DevelopmentProject>();
    public DbSet<DevelopmentTask> DevelopmentTasks => Set<DevelopmentTask>();
    public DbSet<ProjectMilestone> ProjectMilestones => Set<ProjectMilestone>();
    public DbSet<ProjectSprint> ProjectSprints => Set<ProjectSprint>();
    public DbSet<ProjectBug> ProjectBugs => Set<ProjectBug>();
    public DbSet<DeveloperWorklog> DeveloperWorklogs => Set<DeveloperWorklog>();
    public DbSet<RolePermissionOverride> RolePermissionOverrides => Set<RolePermissionOverride>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<LeadStatusApproval>(entity =>
        {
            entity.Property(x => x.State).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Comment).HasMaxLength(500);
            entity.Property(x => x.LeadVersion).HasMaxLength(8).IsRequired();
            entity.HasIndex(x => x.LeadId).IsUnique().HasFilter("[State] = N'Pending'");
            entity.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<LeadStatus>().WithMany().HasForeignKey(x => x.RequestedStatusId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RequestedById).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.DecidedById).OnDelete(DeleteBehavior.NoAction);
            entity.ToTable(t => t.HasCheckConstraint("CK_LeadStatusApprovals_State", "[State] IN (N'Pending', N'Approved', N'Rejected')"));
        });

        modelBuilder.Entity<RolePermissionGrant>(entity =>
        {
            entity.Property(x => x.Permission).HasMaxLength(80).IsRequired();
            entity.HasIndex(x => new { x.RoleId, x.Permission }).IsUnique();
            entity.HasOne<IdentityRole<long>>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.NoAction);
            entity.ToTable(t => t.HasCheckConstraint("CK_RolePermissionGrants_Scope", "[Scope] BETWEEN 0 AND 5"));
        });
        modelBuilder.Entity<UserPermissionRestriction>(entity =>
        {
            entity.Property(x => x.Permission).HasMaxLength(80).IsRequired();
            entity.HasIndex(x => new { x.UserId, x.Permission }).IsUnique();
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Department>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.HasIndex(x => x.Name).IsUnique();
        });
        modelBuilder.Entity<Team>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.HasIndex(x => new { x.DepartmentId, x.Name }).IsUnique();
            entity.HasOne<Department>().WithMany().HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<TeamMembership>(entity =>
        {
            entity.HasIndex(x => new { x.TeamId, x.UserId }).IsUnique();
            entity.HasIndex(x => x.UserId);
            entity.HasOne<Team>().WithMany().HasForeignKey(x => x.TeamId).OnDelete(DeleteBehavior.NoAction);
            entity.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<ApplicationUser>().HasOne<Department>().WithMany()
            .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.NoAction);

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

        modelBuilder.Entity<LeadSource>(entity =>
        {
            entity.Property(x => x.Name).HasMaxLength(60);
            entity.HasIndex(x => x.Name).IsUnique();
        });
        modelBuilder.Entity<LeadStatus>().Property(x => x.Name).HasMaxLength(60);
        modelBuilder.Entity<LeadSource>().HasData(
            new LeadSource { Id = 1, Name = "Manual", IsActive = true },
            new LeadSource { Id = 2, Name = "Website", IsActive = true },
            new LeadSource { Id = 3, Name = "Facebook", IsActive = true },
            new LeadSource { Id = 4, Name = "WhatsApp", IsActive = true });
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
            entity.HasOne(x => x.Milestone).WithMany().HasForeignKey(x => x.MilestoneId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Sprint).WithMany().HasForeignKey(x => x.SprintId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Project).WithMany(x => x.Tasks).HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.AssignedTo).WithMany().HasForeignKey(x => x.AssignedToId)
                .OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<ProjectMilestone>(entity =>
        {
            entity.Property(x => x.Title).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.HasIndex(x => new { x.ProjectId, x.DueDate });
            entity.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ProjectSprint>(entity =>
        {
            entity.Property(x => x.Title).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Goal).HasMaxLength(1000);
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.HasIndex(x => new { x.ProjectId, x.StartDate });
            entity.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ProjectBug>(entity =>
        {
            entity.Property(x => x.Title).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.Status).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Severity).HasMaxLength(10).IsRequired();
            entity.HasIndex(x => new { x.ProjectId, x.Status });
            entity.HasIndex(x => x.AssignedToId);
            entity.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Task).WithMany().HasForeignKey(x => x.TaskId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.AssignedTo).WithMany().HasForeignKey(x => x.AssignedToId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.ReportedBy).WithMany().HasForeignKey(x => x.ReportedById)
                .OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<DeveloperWorklog>(entity =>
        {
            entity.Property(x => x.Hours).HasPrecision(5, 2);
            entity.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            entity.HasIndex(x => new { x.ProjectId, x.WorkDate });
            entity.HasIndex(x => new { x.UserId, x.WorkDate });
            entity.HasOne(x => x.Project).WithMany().HasForeignKey(x => x.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Task).WithMany().HasForeignKey(x => x.TaskId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.NoAction);
        });
        modelBuilder.Entity<RolePermissionOverride>(entity =>
        {
            entity.Property(x => x.RoleName).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Permission).HasMaxLength(60).IsRequired();
            entity.HasIndex(x => new { x.RoleName, x.Permission }).IsUnique();
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
