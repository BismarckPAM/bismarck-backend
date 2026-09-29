using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthorizationService.Migrations
{
    /// <inheritdoc />
    public partial class AddJitVmConnectionContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AzureScope",
                table: "TemporaryPermissions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConnectionCommand",
                table: "TemporaryPermissions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetHost",
                table: "TemporaryPermissions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetOsType",
                table: "TemporaryPermissions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetResourceGroup",
                table: "TemporaryPermissions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetVmName",
                table: "TemporaryPermissions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AzureScope",
                table: "TemporaryPermissions");

            migrationBuilder.DropColumn(
                name: "ConnectionCommand",
                table: "TemporaryPermissions");

            migrationBuilder.DropColumn(
                name: "TargetHost",
                table: "TemporaryPermissions");

            migrationBuilder.DropColumn(
                name: "TargetOsType",
                table: "TemporaryPermissions");

            migrationBuilder.DropColumn(
                name: "TargetResourceGroup",
                table: "TemporaryPermissions");

            migrationBuilder.DropColumn(
                name: "TargetVmName",
                table: "TemporaryPermissions");
        }
    }
}
