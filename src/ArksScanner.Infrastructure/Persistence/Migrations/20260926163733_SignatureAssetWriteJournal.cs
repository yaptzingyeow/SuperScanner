using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArksScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SignatureAssetWriteJournal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "signature_asset_write_intents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssetKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_signature_asset_write_intents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_signature_asset_write_intents_CreatedAt",
                table: "signature_asset_write_intents",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "signature_asset_write_intents");
        }
    }
}
