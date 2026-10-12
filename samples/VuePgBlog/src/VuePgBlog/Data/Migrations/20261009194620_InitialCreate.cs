using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VuePgBlog.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assets", x => x.Id);
                });

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

            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "text", nullable: false),
                    Payload = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    RunAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SearchIndexStates",
                columns: table => new
                {
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedVersion = table.Column<long>(type: "bigint", nullable: false),
                    AppliedVersion = table.Column<long>(type: "bigint", nullable: false),
                    TaskId = table.Column<long>(type: "bigint", nullable: true),
                    TaskVersion = table.Column<long>(type: "bigint", nullable: true),
                    LeaseToken = table.Column<string>(type: "text", nullable: true),
                    LockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchIndexStates", x => x.PostId);
                });

            migrationBuilder.CreateTable(
                name: "SeedCheckpoints",
                columns: table => new
                {
                    Name = table.Column<string>(type: "text", nullable: false),
                    Completed = table.Column<int>(type: "integer", nullable: false),
                    Seed = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeedCheckpoints", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    EmailVerifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AvatarAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDemo = table.Column<bool>(type: "boolean", nullable: false),
                    DemoKey = table.Column<string>(type: "text", nullable: true),
                    NextDigestAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Preferences = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Assets_AvatarAssetId",
                        column: x => x.AvatarAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "DigestDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    TimeZoneId = table.Column<string>(type: "text", nullable: false),
                    WindowStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WindowEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PostIdsJson = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ProviderReference = table.Column<string>(type: "text", nullable: true),
                    LastError = table.Column<string>(type: "text", nullable: true),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DigestDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DigestDeliveries_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FeedSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Mode = table.Column<string>(type: "text", nullable: false),
                    ItemsJson = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedSnapshots_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Follows",
                columns: table => new
                {
                    FollowerId = table.Column<Guid>(type: "uuid", nullable: false),
                    FolloweeId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Follows", x => new { x.FollowerId, x.FolloweeId });
                    table.CheckConstraint("CK_Follow_NotSelf", "\"FollowerId\" <> \"FolloweeId\"");
                    table.ForeignKey(
                        name: "FK_Follows_Users_FolloweeId",
                        column: x => x.FolloweeId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Follows_Users_FollowerId",
                        column: x => x.FollowerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InterestProfiles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicsJson = table.Column<string>(type: "text", nullable: false),
                    AuthorsJson = table.Column<string>(type: "text", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Dirty = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterestProfiles", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_InterestProfiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Posts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Slug = table.Column<string>(type: "text", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    Excerpt = table.Column<string>(type: "text", nullable: false),
                    CoverAssetId = table.Column<Guid>(type: "uuid", nullable: true),
                    LikeCount = table.Column<int>(type: "integer", nullable: false),
                    CommentCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DemoKey = table.Column<string>(type: "text", nullable: true),
                    IndexVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Posts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Posts_Assets_CoverAssetId",
                        column: x => x.CoverAssetId,
                        principalTable: "Assets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Posts_Users_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Bookmarks",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookmarks", x => new { x.UserId, x.PostId });
                    table.ForeignKey(
                        name: "FK_Bookmarks_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Bookmarks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comments_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Comments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Likes",
                columns: table => new
                {
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Likes", x => new { x.PostId, x.UserId });
                    table.ForeignKey(
                        name: "FK_Likes_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Likes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PostDailyStats",
                columns: table => new
                {
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Views = table.Column<int>(type: "integer", nullable: false),
                    UniqueViewers = table.Column<int>(type: "integer", nullable: false),
                    Likes = table.Column<int>(type: "integer", nullable: false),
                    Comments = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostDailyStats", x => new { x.PostId, x.Date });
                    table.ForeignKey(
                        name: "FK_PostDailyStats_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PostTopics",
                columns: table => new
                {
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    Topic = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostTopics", x => new { x.PostId, x.Topic });
                    table.ForeignKey(
                        name: "FK_PostTopics_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PostViews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewerHash = table.Column<string>(type: "text", nullable: false),
                    Referrer = table.Column<string>(type: "text", nullable: true),
                    At = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostViews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PostViews_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Reposts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reposts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reposts_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Reposts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeenPosts",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeenPosts", x => new { x.UserId, x.PostId });
                    table.ForeignKey(
                        name: "FK_SeenPosts_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeenPosts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommentLikes",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentLikes", x => new { x.CommentId, x.UserId });
                    table.ForeignKey(
                        name: "FK_CommentLikes_Comments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "Comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommentLikes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assets_Key",
                table: "Assets",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Assets_Status_CreatedAt",
                table: "Assets",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_PostId",
                table: "Bookmarks",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookmarks_UserId_CreatedAt_PostId",
                table: "Bookmarks",
                columns: new[] { "UserId", "CreatedAt", "PostId" });

            migrationBuilder.CreateIndex(
                name: "IX_CommentLikes_UserId",
                table: "CommentLikes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_PostId_CreatedAt",
                table: "Comments",
                columns: new[] { "PostId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_UserId_CreatedAt_PostId",
                table: "Comments",
                columns: new[] { "UserId", "CreatedAt", "PostId" });

            migrationBuilder.CreateIndex(
                name: "IX_DigestDeliveries_Status_CreatedAt",
                table: "DigestDeliveries",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DigestDeliveries_UserId_LocalDate",
                table: "DigestDeliveries",
                columns: new[] { "UserId", "LocalDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeedSnapshots_ExpiresAt",
                table: "FeedSnapshots",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_FeedSnapshots_UserId",
                table: "FeedSnapshots",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Follows_FolloweeId_FollowerId",
                table: "Follows",
                columns: new[] { "FolloweeId", "FollowerId" });

            migrationBuilder.CreateIndex(
                name: "IX_InterestProfiles_Dirty_UpdatedAt",
                table: "InterestProfiles",
                columns: new[] { "Dirty", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Status_RunAt",
                table: "Jobs",
                columns: new[] { "Status", "RunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Likes_PostId_CreatedAt",
                table: "Likes",
                columns: new[] { "PostId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Likes_UserId_CreatedAt_PostId",
                table: "Likes",
                columns: new[] { "UserId", "CreatedAt", "PostId" });

            migrationBuilder.CreateIndex(
                name: "IX_Posts_AuthorId_CreatedAt",
                table: "Posts",
                columns: new[] { "AuthorId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Posts_CoverAssetId",
                table: "Posts",
                column: "CoverAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_CreatedAt",
                table: "Posts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Posts_CreatedAt_Id",
                table: "Posts",
                columns: new[] { "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Posts_DemoKey",
                table: "Posts",
                column: "DemoKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Posts_PublishedAt_Id",
                table: "Posts",
                columns: new[] { "PublishedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Posts_Slug",
                table: "Posts",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PostTopics_Topic_PostId",
                table: "PostTopics",
                columns: new[] { "Topic", "PostId" });

            migrationBuilder.CreateIndex(
                name: "IX_PostViews_At",
                table: "PostViews",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_PostViews_PostId_At",
                table: "PostViews",
                columns: new[] { "PostId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_Reposts_PostId",
                table: "Reposts",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_Reposts_UserId_CreatedAt_Id",
                table: "Reposts",
                columns: new[] { "UserId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Reposts_UserId_PostId",
                table: "Reposts",
                columns: new[] { "UserId", "PostId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SearchIndexStates_LockedUntil",
                table: "SearchIndexStates",
                column: "LockedUntil");

            migrationBuilder.CreateIndex(
                name: "IX_SeenPosts_PostId",
                table: "SeenPosts",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_SeenPosts_UserId_LastSeenAt",
                table: "SeenPosts",
                columns: new[] { "UserId", "LastSeenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_AvatarAssetId",
                table: "Users",
                column: "AvatarAssetId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_DemoKey",
                table: "Users",
                column: "DemoKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_NextDigestAtUtc",
                table: "Users",
                column: "NextDigestAtUtc");

            migrationBuilder.Sql("""
                CREATE INDEX "IX_Posts_Search" ON "Posts"
                USING GIN (to_tsvector('english', "Title" || ' ' || "Body"));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Posts_Search\";");

            migrationBuilder.DropTable(
                name: "Bookmarks");

            migrationBuilder.DropTable(
                name: "CommentLikes");

            migrationBuilder.DropTable(
                name: "CronLeases");

            migrationBuilder.DropTable(
                name: "DigestDeliveries");

            migrationBuilder.DropTable(
                name: "FeedSnapshots");

            migrationBuilder.DropTable(
                name: "Follows");

            migrationBuilder.DropTable(
                name: "InterestProfiles");

            migrationBuilder.DropTable(
                name: "Jobs");

            migrationBuilder.DropTable(
                name: "Likes");

            migrationBuilder.DropTable(
                name: "PostDailyStats");

            migrationBuilder.DropTable(
                name: "PostTopics");

            migrationBuilder.DropTable(
                name: "PostViews");

            migrationBuilder.DropTable(
                name: "Reposts");

            migrationBuilder.DropTable(
                name: "SearchIndexStates");

            migrationBuilder.DropTable(
                name: "SeedCheckpoints");

            migrationBuilder.DropTable(
                name: "SeenPosts");

            migrationBuilder.DropTable(
                name: "Comments");

            migrationBuilder.DropTable(
                name: "Posts");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Assets");
        }
    }
}
