using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inkwell.Data.Migrations
{
    /// <inheritdoc />
    public partial class DropSearchIndexing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SearchIndexStates");

            // Raw SQL: EF would rebuild Posts for DropColumn, which drops the PostsFts triggers with the old table.
            migrationBuilder.Sql("ALTER TABLE \"Posts\" DROP COLUMN \"IndexVersion\";");
            migrationBuilder.Sql("DELETE FROM \"Jobs\" WHERE \"Type\" IN ('SyncSearchPost', 'SyncSearchPostsBatch', 'ReindexSearchBatch');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "IndexVersion",
                table: "Posts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "SearchIndexStates",
                columns: table => new
                {
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AppliedVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    LeaseToken = table.Column<string>(type: "TEXT", nullable: true),
                    LockedUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RequestedVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    TaskId = table.Column<long>(type: "INTEGER", nullable: true),
                    TaskVersion = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchIndexStates", x => x.PostId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SearchIndexStates_LockedUntil",
                table: "SearchIndexStates",
                column: "LockedUntil");

            migrationBuilder.CreateIndex(
                name: "IX_SearchIndexStates_PostId",
                table: "SearchIndexStates",
                column: "PostId",
                filter: "\"RequestedVersion\" > \"AppliedVersion\"");
        }
    }
}
