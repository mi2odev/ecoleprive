using CentreSoutien.Application.Abstractions;
using CentreSoutien.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace CentreSoutien.Tests;

public class MigrationTests
{
    /// <summary>
    /// A database from before "courses" were merged into groups keeps every group, its students and payments;
    /// each group takes the subject, level and price of its former course.
    /// </summary>
    [Fact]
    public async Task Existing_courses_are_merged_into_their_groups()
    {
        await using var host = await TestHost.CreateAsync(); // latest schema, with owner account
        var factory = host.Get<IDbContextFactory<AppDbContext>>();

        await using (var db = await factory.CreateDbContextAsync())
        {
            // Roll back to the schema that still had Courses, then insert "old" data.
            await db.GetService<IMigrator>().MigrateAsync("20260926113449_ReminderTemplates");
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO "Subjects" ("Id","Name","ShortName","CreatedAt") VALUES (1,'Mathématiques','Maths','2026-09-01'), (2,'Physique',NULL,'2026-09-01');
                INSERT INTO "Courses" ("Id","SubjectId","Level","MonthlyPrice","Description","IsActive","CreatedAt") VALUES
                    (10,1,'3AS','4500.0','Préparation BAC',1,'2026-09-01'),
                    (11,2,'2AS','4000.0',NULL,0,'2026-09-01');
                INSERT INTO "Groups" ("Id","CourseId","Name","Capacity","IsActive","CreatedAt") VALUES
                    (100,10,'A',12,1,'2026-09-01'), (101,10,'B',12,1,'2026-09-01'), (102,11,'A',8,1,'2026-09-01');
                INSERT INTO "Students" ("Id","Matricule","FirstName","LastName","Gender","Level","IsActive","EnrolledOn","CreatedAt")
                    VALUES (1,'E1001','Lina','Kaci',0,'3AS',1,'2026-09-01','2026-09-01');
                INSERT INTO "Enrollments" ("Id","StudentId","GroupId","StartDate","CreatedAt") VALUES (1,1,101,'2026-09-01','2026-09-01');
                """);
            await db.GetService<IMigrator>().MigrateAsync(); // upgrade to the current schema
        }

        var groups = await host.Get<IGroupService>().ListAsync();
        Assert.Equal(3, groups.Count);
        var b = groups.Single(g => g.Id == 101);
        Assert.Equal("Mathématiques · 3AS B", b.FullName);
        Assert.Equal(4500m, b.MonthlyPrice);
        Assert.Equal("Préparation BAC", b.Description);
        Assert.True(b.IsActive);
        Assert.Single(b.Enrollments);

        var physics = groups.Single(g => g.Id == 102);
        Assert.Equal("Physique", physics.Subject!.Name);
        Assert.Equal("2AS", physics.Level);
        Assert.Equal(4000m, physics.MonthlyPrice);
        Assert.False(physics.IsActive); // its course was inactive

        // Billing still uses the (now group) price.
        var students = await host.Get<IStudentService>().ListAsync(new DateTime(2026, 9, 26));
        Assert.Equal(4500m, students.Single().MonthlyDue);
    }
}
