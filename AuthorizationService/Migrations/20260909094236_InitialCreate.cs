using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthorizationService.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccessPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Environment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Criticality = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MaxAccessLevel = table.Column<int>(type: "integer", nullable: false),
                    RequiresApprovalForElevated = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccessPolicies", x => x.Id);
                    table.CheckConstraint("CK_AccessPolicies_MaxAccessLevel", "\"MaxAccessLevel\" BETWEEN 0 AND 5");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccessPolicies_Role_Environment_Criticality",
                table: "AccessPolicies",
                columns: new[] { "Role", "Environment", "Criticality" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccessPolicies");
        }
    }
}
