using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CentreSoutien.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReminderTemplates : Migration
    {
        /// <summary>Existing centers get the French default texts (not empty strings).</summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AbsenceMessageTemplate",
                table: "CenterSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "Bonjour {parent}, nous vous informons que {eleve} était absent(e) au cours de {cours} le {date}. {centre}");

            migrationBuilder.AddColumn<string>(
                name: "PaymentReminderTemplate",
                table: "CenterSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "Bonjour {parent}, sauf erreur de notre part, la mensualité de {mois} pour {eleve} n'est pas encore réglée. Reste à payer : {reste}. Merci de passer au centre. {centre} – {telephone}");

            migrationBuilder.AddColumn<string>(
                name: "PhoneCountryCode",
                table: "CenterSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "213");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AbsenceMessageTemplate",
                table: "CenterSettings");

            migrationBuilder.DropColumn(
                name: "PaymentReminderTemplate",
                table: "CenterSettings");

            migrationBuilder.DropColumn(
                name: "PhoneCountryCode",
                table: "CenterSettings");
        }
    }
}
