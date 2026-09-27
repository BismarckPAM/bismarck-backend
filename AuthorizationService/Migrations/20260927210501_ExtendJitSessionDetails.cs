using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthorizationService.Migrations
{
    /// <inheritdoc />
    public partial class ExtendJitSessionDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Action",
                table: "TemporaryPermissions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CloudRoleAssignmentId",
                table: "TemporaryPermissions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProvisioningDetail",
                table: "TemporaryPermissions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProvisioningStatus",
                table: "TemporaryPermissions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceName",
                table: "TemporaryPermissions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserEmail",
                table: "TemporaryPermissions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Action",
                table: "TemporaryPermissions");

            migrationBuilder.DropColumn(
                name: "CloudRoleAssignmentId",
                table: "TemporaryPermissions");

            migrationBuilder.DropColumn(
                name: "ProvisioningDetail",
                table: "TemporaryPermissions");

            migrationBuilder.DropColumn(
                name: "ProvisioningStatus",
                table: "TemporaryPermissions");

            migrationBuilder.DropColumn(
                name: "ResourceName",
                table: "TemporaryPermissions");

            migrationBuilder.DropColumn(
                name: "UserEmail",
                table: "TemporaryPermissions");
        }
    }
}
