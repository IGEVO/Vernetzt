using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Igevo.Vernetzt.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemoveEventVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Events_Visibility",
                table: "Events");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Event_HighlightPublic",
                table: "Events");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CalendarFeedToken_GlobalPublic",
                table: "CalendarFeedTokens");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "Visibility",
                table: "CalendarFeedTokens");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Visibility",
                table: "Events",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Visibility",
                table: "CalendarFeedTokens",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Events_Visibility",
                table: "Events",
                column: "Visibility");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Event_HighlightPublic",
                table: "Events",
                sql: "(NOT \"IsHighlight\") OR (\"Visibility\" = 1)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CalendarFeedToken_GlobalPublic",
                table: "CalendarFeedTokens",
                sql: "(\"Scope\" <> 2) OR (\"Visibility\" = 1)");
        }
    }
}
