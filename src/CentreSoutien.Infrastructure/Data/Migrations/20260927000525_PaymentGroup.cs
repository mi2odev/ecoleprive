using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentreSoutien.Infrastructure.Data.Migrations
{
    /// <summary>Each student payment for sessions belongs to a group (groups are paid separately).</summary>
    public partial class PaymentGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GroupId",
                table: "StudentPayments",
                type: "INTEGER",
                nullable: true);

            // Existing session payments of a student who only ever had one group belong to that group. Others stay
            // without a group and cover the oldest unpaid sessions of any of the student's groups.
            migrationBuilder.Sql("""
                UPDATE "StudentPayments" SET "GroupId" =
                    (SELECT MIN(e."GroupId") FROM "Enrollments" e WHERE e."StudentId" = "StudentPayments"."StudentId")
                WHERE "Kind" = 0
                  AND (SELECT COUNT(DISTINCT e."GroupId") FROM "Enrollments" e WHERE e."StudentId" = "StudentPayments"."StudentId") = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_StudentPayments_GroupId",
                table: "StudentPayments",
                column: "GroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_StudentPayments_Groups_GroupId",
                table: "StudentPayments",
                column: "GroupId",
                principalTable: "Groups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StudentPayments_Groups_GroupId",
                table: "StudentPayments");

            migrationBuilder.DropIndex(
                name: "IX_StudentPayments_GroupId",
                table: "StudentPayments");

            migrationBuilder.DropColumn(
                name: "GroupId",
                table: "StudentPayments");
        }
    }
}
