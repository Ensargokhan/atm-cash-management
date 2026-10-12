using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtmCash.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AtmStatusChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FromStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ToStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PerformedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AtmStatusChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AtmStatusChanges_Atms_AtmId",
                        column: x => x.AtmId,
                        principalTable: "Atms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeRateChanges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    OldRateToTry = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    NewRateToTry = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    ChangedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRateChanges", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AtmStatusChanges_AtmId_OccurredAt",
                table: "AtmStatusChanges",
                columns: new[] { "AtmId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ExchangeRateChanges_Currency_ChangedAt",
                table: "ExchangeRateChanges",
                columns: new[] { "Currency", "ChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AtmStatusChanges");

            migrationBuilder.DropTable(
                name: "ExchangeRateChanges");
        }
    }
}
