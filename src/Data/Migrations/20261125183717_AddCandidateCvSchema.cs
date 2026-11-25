using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Recruitment.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCandidateCvSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CandidateCvs",
                columns: table => new
                {
                    CvId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReplacedByCvId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CandidateCvs", x => x.CvId);
                    table.ForeignKey(
                        name: "FK_CandidateCvs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CandidateCvs_StorageKey",
                table: "CandidateCvs",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CandidateCvs_UserId_DeletedAtUtc",
                table: "CandidateCvs",
                columns: new[] { "UserId", "DeletedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CandidateCvs");
        }
    }
}
