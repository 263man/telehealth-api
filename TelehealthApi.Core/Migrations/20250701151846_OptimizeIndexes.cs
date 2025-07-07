using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelehealthApi.Core.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Patients_Email",
                table: "Patients");

            migrationBuilder.CreateIndex(
                name: "IX_Patients_Email_FhirPatientId",
                table: "Patients",
                columns: new[] { "Email", "FhirPatientId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp",
                schema: "telehealthkeps",
                table: "AuditLogs",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Patients_Email_FhirPatientId",
                table: "Patients");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_Timestamp",
                schema: "telehealthkeps",
                table: "AuditLogs");

            migrationBuilder.CreateIndex(
                name: "IX_Patients_Email",
                table: "Patients",
                column: "Email");
        }
    }
}
