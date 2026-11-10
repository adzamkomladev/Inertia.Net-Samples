using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechLandier.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCronLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CronLeases");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CronLeases",
                columns: table => new
                {
                    Name = table.Column<string>(type: "text", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CronLeases", x => x.Name);
                });
        }
    }
}
