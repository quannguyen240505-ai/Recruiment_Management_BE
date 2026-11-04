using Microsoft.EntityFrameworkCore;
using Recruitment.Api.Entities;

namespace Recruitment.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<Job> Jobs => Set<Job>();
    public DbSet<JobSkill> JobSkills => Set<JobSkill>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Role>(e =>
        {
            e.ToTable("Role");
            e.HasKey(x => x.RoleId);
            e.Property(x => x.RoleName).IsRequired().HasMaxLength(50);

            // Seed data theo yêu cầu
            e.HasData(
                new Role { RoleId = "CANDIDATE", RoleName = "Candidate", Description = "Ứng viên" },
                new Role { RoleId = "RECRUITER", RoleName = "Recruiter", Description = "Nhà tuyển dụng" },
                new Role { RoleId = "ADMIN", RoleName = "Admin", Description = "Quản trị viên" });

            modelBuilder.Entity<JobSkill>(e =>
            {
                e.HasKey(js => new { js.JobId, js.SkillId });
                e.HasOne(js => js.Job).WithMany(j => j.JobSkills).HasForeignKey(js => js.JobId);
                e.HasOne(js => js.Skill).WithMany(s => s.JobSkills).HasForeignKey(js => js.SkillId);
            });

            modelBuilder.Entity<Job>(e =>
            {
                e.Property(x => x.JobType).HasConversion<string>();
                e.Property(x => x.Status).HasConversion<string>();
                e.Property(x => x.SalaryMin).HasColumnType("decimal(18,2)");
                e.Property(x => x.SalaryMax).HasColumnType("decimal(18,2)");
            });
        });

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("Users");
            e.HasKey(x => x.UserId);

            // Cấu hình Unique (Không phân biệt hoa thường là mặc định của SQL Server)
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.GoogleId).IsUnique(); // Chỉ unique khi có giá trị (SQL Server tự xử lý được nhiều NULL)

            // Cấu hình độ dài và bắt buộc
            e.Property(x => x.Username).IsRequired().HasMaxLength(100);
            e.Property(x => x.Email).IsRequired().HasMaxLength(256);
            e.Property(x => x.FullName).IsRequired().HasMaxLength(200);
            e.Property(x => x.PasswordHash).HasMaxLength(512);
            e.Property(x => x.GoogleId).HasMaxLength(100);

            // Lưu Enum AccountStatus dưới dạng String trong DB cho dễ đọc
            e.Property(x => x.AccountStatus)
             .HasConversion<string>()
             .HasMaxLength(20)
             .IsRequired();

            // Cấu hình quan hệ
            e.HasOne(x => x.Role)
             .WithMany(r => r.Users)
             .HasForeignKey(x => x.RoleId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
