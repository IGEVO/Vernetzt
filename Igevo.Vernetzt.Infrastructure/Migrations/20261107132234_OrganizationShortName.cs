using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Igevo.Vernetzt.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class OrganizationShortName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShortName",
                table: "Departments",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShortName",
                table: "Clubs",
                type: "TEXT",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShortName",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "ShortName",
                table: "Clubs");
        }
    }
}
