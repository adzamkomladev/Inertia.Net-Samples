using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReactSqliteReplicasBlog.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplicaCronLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CronLeases",
                columns: table => new
                {
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LockedUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Token = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CronLeases", x => x.Name);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CronLeases");
        }
    }
}
