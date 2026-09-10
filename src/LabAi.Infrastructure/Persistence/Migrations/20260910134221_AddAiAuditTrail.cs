using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabAi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAiAuditTrail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ai_audit_entries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ActorUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    QuestionMasked = table.Column<string>(type: "TEXT", nullable: false),
                    RetrievedChunkIds = table.Column<string>(type: "TEXT", nullable: false),
                    TopScore = table.Column<double>(type: "REAL", nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PromptHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AnsweredFromContext = table.Column<bool>(type: "INTEGER", nullable: false),
                    RefusalStage = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AnswerMasked = table.Column<string>(type: "TEXT", nullable: false),
                    Rationale = table.Column<string>(type: "TEXT", nullable: true),
                    PrevHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RowHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_audit_entries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_audit_entries_CorrelationId",
                table: "ai_audit_entries",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_ai_audit_entries_TimestampUtc",
                table: "ai_audit_entries",
                column: "TimestampUtc");

            // Append-only at the database level: the application only ever inserts, but a trigger
            // makes tampering impossible even for a caller with direct SQLite access. RAISE(ABORT)
            // rolls back the offending statement and surfaces an error to the writer.
            migrationBuilder.Sql("""
                CREATE TRIGGER ai_audit_entries_no_update
                BEFORE UPDATE ON ai_audit_entries
                BEGIN
                    SELECT RAISE(ABORT, 'ai_audit_entries is append-only: updates are forbidden');
                END;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER ai_audit_entries_no_delete
                BEFORE DELETE ON ai_audit_entries
                BEGIN
                    SELECT RAISE(ABORT, 'ai_audit_entries is append-only: deletes are forbidden');
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS ai_audit_entries_no_update;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS ai_audit_entries_no_delete;");

            migrationBuilder.DropTable(
                name: "ai_audit_entries");
        }
    }
}
