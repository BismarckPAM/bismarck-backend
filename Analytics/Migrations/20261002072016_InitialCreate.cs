using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Analytics.Service.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AnalyticsEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceTopic = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OccurredAtDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Actor = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Resource = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    ResourceName = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DenialReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Metadata = table.Column<string>(type: "jsonb", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnalyticsEvents", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_DenialReason",
                table: "AnalyticsEvents",
                column: "DenialReason");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_EventId",
                table: "AnalyticsEvents",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_EventType_OccurredAt",
                table: "AnalyticsEvents",
                columns: new[] { "EventType", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_OccurredAt",
                table: "AnalyticsEvents",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_OccurredAtDate_EventType",
                table: "AnalyticsEvents",
                columns: new[] { "OccurredAtDate", "EventType" });

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_ResourceName",
                table: "AnalyticsEvents",
                column: "ResourceName");

            migrationBuilder.CreateIndex(
                name: "IX_AnalyticsEvents_SourceTopic_OccurredAt",
                table: "AnalyticsEvents",
                columns: new[] { "SourceTopic", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AnalyticsEvents");
        }
    }
}
