using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LabAi.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Username = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    FullName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PasswordSalt = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "source_documents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourcePath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    SupersededByDocumentId = table.Column<long>(type: "INTEGER", nullable: true),
                    IngestedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IngestedByUserId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_source_documents_source_documents_SupersededByDocumentId",
                        column: x => x.SupersededByDocumentId,
                        principalTable: "source_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_source_documents_users_IngestedByUserId",
                        column: x => x.IngestedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "chunks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DocumentId = table.Column<long>(type: "INTEGER", nullable: false),
                    ChunkIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    SectionPath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    PageStart = table.Column<int>(type: "INTEGER", nullable: true),
                    PageEnd = table.Column<int>(type: "INTEGER", nullable: true),
                    LineStart = table.Column<int>(type: "INTEGER", nullable: true),
                    LineEnd = table.Column<int>(type: "INTEGER", nullable: true),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EmbeddingModelId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    EmbeddingDimension = table.Column<int>(type: "INTEGER", nullable: false),
                    Embedding = table.Column<byte[]>(type: "BLOB", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_chunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_chunks_source_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "source_documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_chunks_DocumentId_ChunkIndex",
                table: "chunks",
                columns: new[] { "DocumentId", "ChunkIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_chunks_EmbeddingModelId_EmbeddingDimension",
                table: "chunks",
                columns: new[] { "EmbeddingModelId", "EmbeddingDimension" });

            migrationBuilder.CreateIndex(
                name: "IX_source_documents_ContentHash",
                table: "source_documents",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_source_documents_IngestedByUserId",
                table: "source_documents",
                column: "IngestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_source_documents_Status",
                table: "source_documents",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_source_documents_SupersededByDocumentId",
                table: "source_documents",
                column: "SupersededByDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_users_Username",
                table: "users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chunks");

            migrationBuilder.DropTable(
                name: "source_documents");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
