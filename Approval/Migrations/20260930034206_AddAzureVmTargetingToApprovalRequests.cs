using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Approval.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddAzureVmTargetingToApprovalRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AzureResourceGroup",
                table: "ApprovalRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AzureVmName",
                table: "ApprovalRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsType",
                table: "ApprovalRequests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicHost",
                table: "ApprovalRequests",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AzureResourceGroup",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "AzureVmName",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "OsType",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "PublicHost",
                table: "ApprovalRequests");
        }
    }
}
