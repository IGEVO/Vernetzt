using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Igevo.Vernetzt.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InvitationContextEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContextEmail",
                table: "InvitationTokens",
                type: "TEXT",
                maxLength: 255,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContextEmail",
                table: "InvitationTokens");
        }
    }
}
