using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentreSoutien.Infrastructure.Data.Migrations
{
    /// <summary>
    /// Drops the old Courses table, now that <see cref="MergeCoursesIntoGroups"/> has copied it into the groups and
    /// rebuilt Groups without a reference to it.
    /// </summary>
    public partial class DropCourses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "Courses";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // MergeCoursesIntoGroups.Down recreates the table from the groups.
        }
    }
}
