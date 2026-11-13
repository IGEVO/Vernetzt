using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Igevo.Vernetzt.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AssignmentVisibilityConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The context email visibility becomes the visibility of the whole assignment
            migrationBuilder.RenameColumn(
                name: "ContextEmailVisibility",
                table: "ContactAssignments",
                newName: "Visibility");

            migrationBuilder.AddColumn<Guid>(
                name: "AssignedById",
                table: "ContactAssignments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmedAt",
                table: "ContactAssignments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastConsentRequestAt",
                table: "ContactAssignments",
                type: "TEXT",
                nullable: true);

            // Existing assignments count as confirmed and keep the visibility of their club/department
            // (legacy Internal = AllUsers). A role/office address that was more restrictive than that must not
            // become more visible, so the stricter of both levels applies when one is set.
            // VisibilityLevel: Internal = 0, AllUsers = 1, Public = 2.
            migrationBuilder.Sql("""
                UPDATE "ContactAssignments"
                SET "Visibility" = CASE
                        WHEN "ContextEmail" IS NOT NULL AND TRIM("ContextEmail") <> ''
                            THEN MIN("Visibility", MAX(1, COALESCE(
                                (SELECT c."ContactPersonsVisibility" FROM "Clubs" c WHERE c."Id" = "ContactAssignments"."ClubId"),
                                (SELECT d."ContactPersonsVisibility" FROM "Departments" d WHERE d."Id" = "ContactAssignments"."DepartmentId"),
                                1)))
                        ELSE MAX(1, COALESCE(
                                (SELECT c."ContactPersonsVisibility" FROM "Clubs" c WHERE c."Id" = "ContactAssignments"."ClubId"),
                                (SELECT d."ContactPersonsVisibility" FROM "Departments" d WHERE d."Id" = "ContactAssignments"."DepartmentId"),
                                1))
                    END,
                    "ConfirmedAt" = "AssignedAt";
                """);

            migrationBuilder.DropColumn(
                name: "ContactPersonsVisibility",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "ContactPersonsVisibility",
                table: "Clubs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssignedById",
                table: "ContactAssignments");

            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                table: "ContactAssignments");

            migrationBuilder.DropColumn(
                name: "LastConsentRequestAt",
                table: "ContactAssignments");

            migrationBuilder.RenameColumn(
                name: "Visibility",
                table: "ContactAssignments",
                newName: "ContextEmailVisibility");

            migrationBuilder.AddColumn<int>(
                name: "ContactPersonsVisibility",
                table: "Departments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "ContactPersonsVisibility",
                table: "Clubs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);
        }
    }
}
