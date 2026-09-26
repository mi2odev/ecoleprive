using CentreSoutien.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CentreSoutien.Infrastructure.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<OwnerAccount> Accounts => Set<OwnerAccount>();
    public DbSet<CenterSettings> Settings => Set<CenterSettings>();
    public DbSet<Parent> Parents => Set<Parent>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Teacher> Teachers => Set<Teacher>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<ScheduleSlot> Slots => Set<ScheduleSlot>();
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<Session> Sessions => Set<Session>();
    public DbSet<AttendanceRecord> Attendance => Set<AttendanceRecord>();
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<Grade> Grades => Set<Grade>();
    public DbSet<Discount> Discounts => Set<Discount>();
    public DbSet<StudentPayment> StudentPayments => Set<StudentPayment>();
    public DbSet<TeacherPayment> TeacherPayments => Set<TeacherPayment>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<Document> Documents => Set<Document>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<OwnerAccount>(e =>
        {
            e.ToTable("OwnerAccount");
            e.Property(x => x.Username).HasMaxLength(64).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
            e.Ignore(x => x.Initials);
        });
        b.Entity<CenterSettings>().ToTable("CenterSettings");

        b.Entity<Parent>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(120).IsRequired();
            e.HasIndex(x => x.Phone);
        });

        b.Entity<Student>(e =>
        {
            e.Property(x => x.Matricule).HasMaxLength(20).IsRequired();
            e.HasIndex(x => x.Matricule).IsUnique();
            e.Property(x => x.FirstName).HasMaxLength(80).IsRequired();
            e.Property(x => x.LastName).HasMaxLength(80).IsRequired();
            e.Property(x => x.Level).HasMaxLength(20);
            e.HasOne(x => x.Parent).WithMany(p => p.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Discount).WithMany().HasForeignKey(x => x.DiscountId).OnDelete(DeleteBehavior.SetNull);
            e.Ignore(x => x.FullName);
            e.Ignore(x => x.Initials);
        });

        b.Entity<Teacher>(e =>
        {
            e.Property(x => x.FirstName).HasMaxLength(80).IsRequired();
            e.Property(x => x.LastName).HasMaxLength(80).IsRequired();
            e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.SetNull);
            e.Ignore(x => x.FullName);
            e.Ignore(x => x.Initials);
        });

        b.Entity<Subject>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(80).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
            e.Ignore(x => x.Display);
        });

        b.Entity<Room>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(60).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
        });

        b.Entity<Group>(e =>
        {
            e.ToTable("Groups");
            e.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Level).HasMaxLength(20);
            e.HasOne(x => x.Teacher).WithMany(t => t.Groups).HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.SubjectId, x.Level, x.Name }).IsUnique();
            e.Ignore(x => x.FullName);
            e.Ignore(x => x.SubjectLevel);
            e.Ignore(x => x.PriceLabel);
        });

        b.Entity<ScheduleSlot>(e =>
        {
            e.HasOne(x => x.Group).WithMany(g => g.Slots).HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<Enrollment>(e =>
        {
            e.HasOne(x => x.Student).WithMany(s => s.Enrollments).HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Group).WithMany(g => g.Enrollments).HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.StudentId, x.GroupId });
        });

        b.Entity<Session>(e =>
        {
            e.HasOne(x => x.Group).WithMany(g => g.Sessions).HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Room).WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.GroupId, x.Date, x.Start }).IsUnique();
            e.HasIndex(x => x.Date);
            e.Ignore(x => x.StartsAt);
            e.Ignore(x => x.EndsAt);
        });

        b.Entity<AttendanceRecord>(e =>
        {
            e.HasOne(x => x.Session).WithMany(s => s.Attendance).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.SessionId, x.StudentId }).IsUnique();
        });

        b.Entity<Exam>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(120).IsRequired();
            e.HasOne(x => x.Group).WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Grade>(e =>
        {
            e.HasOne(x => x.Exam).WithMany(x => x.Grades).HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ExamId, x.StudentId }).IsUnique();
        });

        b.Entity<Discount>(e => e.Property(x => x.Name).HasMaxLength(80).IsRequired());

        b.Entity<StudentPayment>(e =>
        {
            e.Property(x => x.ReceiptNumber).HasMaxLength(40).IsRequired();
            e.HasIndex(x => x.ReceiptNumber).IsUnique();
            e.HasOne(x => x.Student).WithMany(s => s.Payments).HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.Date);
        });

        b.Entity<TeacherPayment>(e =>
            e.HasOne(x => x.Teacher).WithMany(t => t.Payments).HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict));

        b.Entity<Expense>(e =>
        {
            e.Property(x => x.Category).HasMaxLength(60);
            e.HasIndex(x => x.Date);
        });

        b.Entity<Document>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(160).IsRequired();
            e.HasIndex(x => new { x.OwnerType, x.OwnerId });
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        Stamp();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        Stamp();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void Stamp()
    {
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added) entry.Entity.CreatedAt = DateTime.Now;
            else if (entry.State == EntityState.Modified) entry.Entity.UpdatedAt = DateTime.Now;
        }
    }
}
