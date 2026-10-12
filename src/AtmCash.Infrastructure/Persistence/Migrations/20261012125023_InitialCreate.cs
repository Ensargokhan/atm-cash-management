using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace AtmCash.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Atms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PendingReconciliationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Atms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExchangeRates",
                columns: table => new
                {
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    RateToTry = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExchangeRates", x => x.Currency);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ResponseJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cassettes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slot = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: true),
                    Denomination = table.Column<int>(type: "int", nullable: true),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    ReservedCount = table.Column<int>(type: "int", nullable: false),
                    IsLowCash = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cassettes", x => x.Id);
                    table.CheckConstraint("CK_Cassettes_Count", "[Count] >= 0 AND [Count] <= [Capacity]");
                    table.CheckConstraint("CK_Cassettes_Reserved", "[ReservedCount] >= 0 AND [ReservedCount] <= [Count]");
                    table.ForeignKey(
                        name: "FK_Cassettes_Atms_AtmId",
                        column: x => x.AtmId,
                        principalTable: "Atms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Quotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CardHash = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    RequestedAmount = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReservedAmount = table.Column<int>(type: "int", nullable: false),
                    ConfirmedOption = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    DispensedAmount = table.Column<int>(type: "int", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Options = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Quotes_Atms_AtmId",
                        column: x => x.AtmId,
                        principalTable: "Atms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Reconciliations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SubmittedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    SubmittedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReviewedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    TotalDifferenceTry = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reconciliations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reconciliations_Atms_AtmId",
                        column: x => x.AtmId,
                        principalTable: "Atms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CassetteMovements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AtmId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CassetteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slot = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Denomination = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    CountDelta = table.Column<int>(type: "int", nullable: false),
                    ReservedDelta = table.Column<int>(type: "int", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PerformedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CassetteMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CassetteMovements_Atms_AtmId",
                        column: x => x.AtmId,
                        principalTable: "Atms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CassetteMovements_Cassettes_CassetteId",
                        column: x => x.CassetteId,
                        principalTable: "Cassettes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "DepositCassetteItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Denomination = table.Column<int>(type: "int", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    CassetteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepositCassetteItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DepositCassetteItems_Cassettes_CassetteId",
                        column: x => x.CassetteId,
                        principalTable: "Cassettes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QuoteReservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    CassetteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slot = table.Column<int>(type: "int", nullable: false),
                    Denomination = table.Column<int>(type: "int", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    QuoteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuoteReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QuoteReservations_Quotes_QuoteId",
                        column: x => x.QuoteId,
                        principalTable: "Quotes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CassetteId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Slot = table.Column<int>(type: "int", nullable: false),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    Denomination = table.Column<int>(type: "int", nullable: false),
                    SystemCount = table.Column<int>(type: "int", nullable: false),
                    CountedCount = table.Column<int>(type: "int", nullable: false),
                    Difference = table.Column<int>(type: "int", nullable: false),
                    DifferenceValueTry = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ReconciliationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReconciliationLines_Reconciliations_ReconciliationId",
                        column: x => x.ReconciliationId,
                        principalTable: "Reconciliations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "ExchangeRates",
                columns: new[] { "Currency", "RateToTry", "UpdatedAt" },
                values: new object[,]
                {
                    { "EUR", 55.16m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "TRY", 1m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { "USD", 49.33m, new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Atms_Code",
                table: "Atms",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CassetteMovements_AtmId_CreatedAt",
                table: "CassetteMovements",
                columns: new[] { "AtmId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CassetteMovements_CassetteId",
                table: "CassetteMovements",
                column: "CassetteId");

            migrationBuilder.CreateIndex(
                name: "IX_CassetteMovements_ReferenceId",
                table: "CassetteMovements",
                column: "ReferenceId");

            migrationBuilder.CreateIndex(
                name: "IX_Cassettes_AtmId_Slot",
                table: "Cassettes",
                columns: new[] { "AtmId", "Slot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DepositCassetteItems_CassetteId_Currency_Denomination",
                table: "DepositCassetteItems",
                columns: new[] { "CassetteId", "Currency", "Denomination" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_OccurredAt",
                table: "OutboxMessages",
                column: "OccurredAt",
                filter: "[SentAt] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_QuoteReservations_QuoteId",
                table: "QuoteReservations",
                column: "QuoteId");

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_AtmId_Status",
                table: "Quotes",
                columns: new[] { "AtmId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_CardHash_Currency_Status",
                table: "Quotes",
                columns: new[] { "CardHash", "Currency", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotes_Status_ExpiresAt",
                table: "Quotes",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationLines_ReconciliationId",
                table: "ReconciliationLines",
                column: "ReconciliationId");

            migrationBuilder.CreateIndex(
                name: "IX_Reconciliations_AtmId_SubmittedAt",
                table: "Reconciliations",
                columns: new[] { "AtmId", "SubmittedAt" });

            // The movement ledger is append-only: reject UPDATE and DELETE at the database level,
            // so the ledger stays trustworthy even for writes that bypass the application.
            migrationBuilder.Sql("""
                CREATE TRIGGER [TR_CassetteMovements_AppendOnly]
                ON [CassetteMovements]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, 'CassetteMovements is an append-only ledger; rows cannot be updated or deleted.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [TR_CassetteMovements_AppendOnly];");

            migrationBuilder.DropTable(
                name: "CassetteMovements");

            migrationBuilder.DropTable(
                name: "DepositCassetteItems");

            migrationBuilder.DropTable(
                name: "ExchangeRates");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords");

            migrationBuilder.DropTable(
                name: "OutboxMessages");

            migrationBuilder.DropTable(
                name: "QuoteReservations");

            migrationBuilder.DropTable(
                name: "ReconciliationLines");

            migrationBuilder.DropTable(
                name: "Cassettes");

            migrationBuilder.DropTable(
                name: "Quotes");

            migrationBuilder.DropTable(
                name: "Reconciliations");

            migrationBuilder.DropTable(
                name: "Atms");
        }
    }
}
