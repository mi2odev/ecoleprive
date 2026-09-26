using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentreSoutien.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MergeCoursesIntoGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. New columns on Groups.
            migrationBuilder.AddColumn<int>(name: "SubjectId", table: "Groups", type: "INTEGER", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<string>(name: "Description", table: "Groups", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>(name: "Level", table: "Groups", type: "TEXT", maxLength: 20, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<decimal>(name: "MonthlyPrice", table: "Groups", type: "TEXT", nullable: false, defaultValue: 0m);

            // 2. Copy each course's subject, level, price and description into its groups, and keep an inactive
            //    course's groups inactive. Nothing is lost.
            migrationBuilder.Sql("""
                UPDATE "Groups" SET
                    "SubjectId" = (SELECT c."SubjectId" FROM "Courses" c WHERE c."Id" = "Groups"."CourseId"),
                    "Level" = COALESCE((SELECT c."Level" FROM "Courses" c WHERE c."Id" = "Groups"."CourseId"), ''),
                    "MonthlyPrice" = COALESCE((SELECT c."MonthlyPrice" FROM "Courses" c WHERE c."Id" = "Groups"."CourseId"), '0.0'),
                    "Description" = (SELECT c."Description" FROM "Courses" c WHERE c."Id" = "Groups"."CourseId"),
                    "IsActive" = "IsActive" AND COALESCE((SELECT c."IsActive" FROM "Courses" c WHERE c."Id" = "Groups"."CourseId"), 1);
                """);

            // 3. Drop the course level.
            migrationBuilder.DropForeignKey(name: "FK_Groups_Courses_CourseId", table: "Groups");
            migrationBuilder.DropIndex(name: "IX_Groups_CourseId_Name", table: "Groups");
            migrationBuilder.DropColumn(name: "CourseId", table: "Groups");

            migrationBuilder.CreateIndex(
                name: "IX_Groups_SubjectId_Level_Name",
                table: "Groups",
                columns: new[] { "SubjectId", "Level", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Groups_Subjects_SubjectId",
                table: "Groups",
                column: "SubjectId",
                principalTable: "Subjects",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // The Courses table itself is dropped by the next migration (DropCourses): EF rebuilds Groups at the very end
            // of this one, and dropping Courses before that would cascade-delete every group and enrollment.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "FK_Groups_Subjects_SubjectId", table: "Groups");
            migrationBuilder.DropIndex(name: "IX_Groups_SubjectId_Level_Name", table: "Groups");

            migrationBuilder.CreateTable(
                name: "Courses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SubjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Level = table.Column<string>(type: "TEXT", nullable: false),
                    MonthlyPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Courses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Courses_Subjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "Subjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // Rebuild one course per subject + level from the groups (price/description of the first group).
            migrationBuilder.Sql("""
                INSERT INTO "Courses" ("SubjectId", "Level", "MonthlyPrice", "Description", "IsActive", "CreatedAt")
                SELECT g."SubjectId", g."Level", g."MonthlyPrice", g."Description", 1, g."CreatedAt"
                FROM "Groups" g
                WHERE g."Id" = (SELECT MIN(x."Id") FROM "Groups" x WHERE x."SubjectId" = g."SubjectId" AND x."Level" = g."Level");
                """);

            migrationBuilder.AddColumn<int>(name: "CourseId", table: "Groups", type: "INTEGER", nullable: false, defaultValue: 0);
            migrationBuilder.Sql("""
                UPDATE "Groups" SET "CourseId" =
                    (SELECT c."Id" FROM "Courses" c WHERE c."SubjectId" = "Groups"."SubjectId" AND c."Level" = "Groups"."Level");
                """);

            migrationBuilder.DropColumn(name: "SubjectId", table: "Groups");
            migrationBuilder.DropColumn(name: "Description", table: "Groups");
            migrationBuilder.DropColumn(name: "Level", table: "Groups");
            migrationBuilder.DropColumn(name: "MonthlyPrice", table: "Groups");

            migrationBuilder.CreateIndex(
                name: "IX_Groups_CourseId_Name",
                table: "Groups",
                columns: new[] { "CourseId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Courses_SubjectId_Level",
                table: "Courses",
                columns: new[] { "SubjectId", "Level" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Groups_Courses_CourseId",
                table: "Groups",
                column: "CourseId",
                principalTable: "Courses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
