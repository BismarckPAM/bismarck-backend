using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthorizationService.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceTypeAndUniquePolicyKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccessPolicies_Role_Environment_Criticality",
                table: "AccessPolicies");

            migrationBuilder.AddColumn<string>(
                name: "ResourceType",
                table: "AccessPolicies",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_AccessPolicies_Role_ResourceType_Environment_Criticality",
                table: "AccessPolicies",
                columns: new[] { "Role", "ResourceType", "Environment", "Criticality" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AccessPolicies_Role_ResourceType_Environment_Criticality",
                table: "AccessPolicies");

            migrationBuilder.DropColumn(
                name: "ResourceType",
                table: "AccessPolicies");

            migrationBuilder.CreateIndex(
                name: "IX_AccessPolicies_Role_Environment_Criticality",
                table: "AccessPolicies",
                columns: new[] { "Role", "Environment", "Criticality" });
        }
    }
}
