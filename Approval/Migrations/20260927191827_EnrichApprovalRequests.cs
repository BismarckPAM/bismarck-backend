using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Approval.Service.Migrations
{
    /// <inheritdoc />
    public partial class EnrichApprovalRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Action",
                table: "ApprovalRequests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequesterEmail",
                table: "ApprovalRequests",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequesterName",
                table: "ApprovalRequests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceName",
                table: "ApprovalRequests",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourceType",
                table: "ApprovalRequests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "ApprovalRequests",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Action",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "RequesterEmail",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "RequesterName",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "ResourceName",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "ResourceType",
                table: "ApprovalRequests");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "ApprovalRequests");
        }
    }
}
