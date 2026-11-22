using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inkwell.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPostRepostCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RepostCount",
                table: "Posts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Backfill the denormalized counter from the existing repost rows.
            migrationBuilder.Sql("""UPDATE "Posts" SET "RepostCount" = (SELECT COUNT(*) FROM "Reposts" WHERE "Reposts"."PostId" = "Posts"."Id")""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RepostCount",
                table: "Posts");
        }
    }
}
