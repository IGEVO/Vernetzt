using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Igevo.Vernetzt.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AccountEmailChange : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountEmailChangeTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    NewEmail = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    OldEmailSnapshot = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsUsed = table.Column<bool>(type: "INTEGER", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountEmailChangeTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountEmailChangeTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmailChangeTokens_ExpiresAt",
                table: "AccountEmailChangeTokens",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmailChangeTokens_TokenHash",
                table: "AccountEmailChangeTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmailChangeTokens_UserId",
                table: "AccountEmailChangeTokens",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountEmailChangeTokens");
        }
    }
}
