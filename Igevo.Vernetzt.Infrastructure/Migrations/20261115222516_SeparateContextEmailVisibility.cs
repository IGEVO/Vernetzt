using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Igevo.Vernetzt.Infrastructure.Migrations
{
    /// <summary>
    /// Separates the visibility of the role/office address from the visibility of the assignment.
    /// </summary>
    public partial class SeparateContextEmailVisibility : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ContextEmailVisibility",
                table: "ContactAssignments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Until now one setting covered both the assignment and its role/office address:
            // keep exactly that visibility for the address, so nothing becomes more (or less) visible.
            migrationBuilder.Sql("""UPDATE "ContactAssignments" SET "ContextEmailVisibility" = "Visibility";""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContextEmailVisibility",
                table: "ContactAssignments");
        }
    }
}
