using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Resource.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddAzureVmTargeting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AzureResourceGroup",
                table: "Resources",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AzureResourceId",
                table: "Resources",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AzureVmName",
                table: "Resources",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OsType",
                table: "Resources",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicHost",
                table: "Resources",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AzureResourceGroup",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "AzureResourceId",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "AzureVmName",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "OsType",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "PublicHost",
                table: "Resources");
        }
    }
}
