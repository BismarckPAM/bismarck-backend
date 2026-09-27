using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Identity.Service.Migrations
{
    /// <inheritdoc />
    public partial class AddOnboardingTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OnboardingTickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Department = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    RequestedRole = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Justification = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ProvisionedUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OnboardingTickets", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingTickets_Email",
                table: "OnboardingTickets",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_OnboardingTickets_Status",
                table: "OnboardingTickets",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OnboardingTickets");
        }
    }
}
