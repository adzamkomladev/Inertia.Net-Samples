using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VuePgReplicasAdvBlog.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Posts_CreatedAt",
                table: "Posts");

            migrationBuilder.DropIndex(
                name: "IX_FeedSnapshots_UserId",
                table: "FeedSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_Comments_PostId_CreatedAt",
                table: "Comments");

            migrationBuilder.CreateIndex(
                name: "IX_SearchIndexStates_PostId",
                table: "SearchIndexStates",
                column: "PostId",
                filter: "\"RequestedVersion\" > \"AppliedVersion\"");

            migrationBuilder.CreateIndex(
                name: "IX_FeedSnapshots_UserId_Mode_CreatedAt",
                table: "FeedSnapshots",
                columns: new[] { "UserId", "Mode", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_PostId_CreatedAt_Id",
                table: "Comments",
                columns: new[] { "PostId", "CreatedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SearchIndexStates_PostId",
                table: "SearchIndexStates");

            migrationBuilder.DropIndex(
                name: "IX_FeedSnapshots_UserId_Mode_CreatedAt",
                table: "FeedSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_Comments_PostId_CreatedAt_Id",
                table: "Comments");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_CreatedAt",
                table: "Posts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FeedSnapshots_UserId",
                table: "FeedSnapshots",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_PostId_CreatedAt",
                table: "Comments",
                columns: new[] { "PostId", "CreatedAt" });
        }
    }
}
