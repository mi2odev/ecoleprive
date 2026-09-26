using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentreSoutien.Infrastructure.Data.Migrations
{
    /// <summary>Groups are paid by packs of sessions (price for N sessions) instead of per month.</summary>
    public partial class SessionPacks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MonthlyPrice",
                table: "Groups",
                newName: "Price");

            migrationBuilder.AddColumn<int>(
                name: "SessionsPerPack",
                table: "Groups",
                type: "INTEGER",
                nullable: false,
                defaultValue: 4);

            // Groups were billed per month: the price stays and becomes the price of about one month of sessions
            // (4 weeks × the group's weekly sessions; 4 when it has no timetable yet).
            migrationBuilder.Sql("""
                UPDATE "Groups" SET "SessionsPerPack" =
                    MAX(4, 4 * (SELECT COUNT(*) FROM "Slots" s WHERE s."GroupId" = "Groups"."Id"));
                """);

            // The default payment reminder spoke of a monthly fee: switch it to the new default (custom texts are kept).
            migrationBuilder.Sql($"""
                UPDATE "CenterSettings" SET "PaymentReminderTemplate" = '{Sql(Domain.Entities.CenterSettings.DefaultPaymentReminderTemplate)}'
                WHERE "PaymentReminderTemplate" = '{Sql(Domain.Entities.CenterSettings.OldMonthlyPaymentReminderTemplate)}';
                """);
        }

        private static string Sql(string text) => text.Replace("'", "''");

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SessionsPerPack",
                table: "Groups");

            migrationBuilder.RenameColumn(
                name: "Price",
                table: "Groups",
                newName: "MonthlyPrice");
        }
    }
}
