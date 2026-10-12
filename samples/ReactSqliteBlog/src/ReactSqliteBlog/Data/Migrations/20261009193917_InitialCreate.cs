using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ReactSqliteBlog.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IsDemo = table.Column<bool>(type: "INTEGER", nullable: false),
                    DemoKey = table.Column<string>(type: "TEXT", nullable: true),
                    NextDigestAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    AvatarUrl = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Preferences_DailyRecommendationDigest = table.Column<bool>(type: "INTEGER", nullable: false),
                    Preferences_TimeZoneId = table.Column<string>(type: "TEXT", nullable: false),
                    Preferences_Theme = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false, defaultValue: "system"),
                    Preferences_EmailOnComment = table.Column<bool>(type: "INTEGER", nullable: false),
                    Preferences_EmailOnLike = table.Column<bool>(type: "INTEGER", nullable: false),
                    Preferences_WeeklyDigest = table.Column<bool>(type: "INTEGER", nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Payload = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    RunAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SearchIndexStates",
                columns: table => new
                {
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestedVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    AppliedVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    TaskId = table.Column<long>(type: "INTEGER", nullable: true),
                    TaskVersion = table.Column<long>(type: "INTEGER", nullable: true),
                    LeaseToken = table.Column<string>(type: "TEXT", nullable: true),
                    LockedUntil = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchIndexStates", x => x.PostId);
                });

            migrationBuilder.CreateTable(
                name: "SeedCheckpoints",
                columns: table => new
                {
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Completed = table.Column<int>(type: "INTEGER", nullable: false),
                    Seed = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeedCheckpoints", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DigestDeliveries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    TimeZoneId = table.Column<string>(type: "TEXT", nullable: false),
                    WindowStartUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    WindowEndUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PostIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderReference = table.Column<string>(type: "TEXT", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DigestDeliveries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DigestDeliveries_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FeedSnapshots",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", nullable: false),
                    ItemsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeedSnapshots_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Follows",
                columns: table => new
                {
                    FollowerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FolloweeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Follows", x => new { x.FollowerId, x.FolloweeId });
                    table.CheckConstraint("CK_Follow_NotSelf", "\"FollowerId\" <> \"FolloweeId\"");
                    table.ForeignKey(
                        name: "FK_Follows_AspNetUsers_FolloweeId",
                        column: x => x.FolloweeId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Follows_AspNetUsers_FollowerId",
                        column: x => x.FollowerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InterestProfiles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TopicsJson = table.Column<string>(type: "TEXT", nullable: false),
                    AuthorsJson = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Dirty = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InterestProfiles", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_InterestProfiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Posts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    DemoKey = table.Column<string>(type: "TEXT", nullable: true),
                    IndexVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    AuthorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    CoverUrl = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ViewCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LikeCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CommentCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Posts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Posts_AspNetUsers_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Bookmarks",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookmarks", x => new { x.UserId, x.PostId });
                    table.ForeignKey(
                        name: "FK_Bookmarks_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Bookmarks_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comments_AspNetUsers_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Comments_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PostLikes",
                columns: table => new
                {
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostLikes", x => new { x.PostId, x.UserId });
                    table.ForeignKey(
                        name: "FK_PostLikes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PostLikes_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PostTopics",
                columns: table => new
                {
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Topic = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
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
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VisitorHash = table.Column<string>(type: "TEXT", nullable: false),
                    Day = table.Column<DateOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostViews", x => new { x.PostId, x.VisitorHash, x.Day });
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reposts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Reposts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Reposts_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SeenPosts",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PostId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FirstSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeenPosts", x => new { x.UserId, x.PostId });
                    table.ForeignKey(
                        name: "FK_SeenPosts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SeenPosts_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Uploads",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    PostId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsAvatar = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Uploads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Uploads_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Uploads_Posts_PostId",
                        column: x => x.PostId,
                        principalTable: "Posts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CommentLikes",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CommentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentLikes", x => new { x.CommentId, x.UserId });
                    table.ForeignKey(
                        name: "FK_CommentLikes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommentLikes_Comments_CommentId",
                        column: x => x.CommentId,
                        principalTable: "Comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_DemoKey",
                table: "AspNetUsers",
                column: "DemoKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_NextDigestAtUtc",
                table: "AspNetUsers",
                column: "NextDigestAtUtc");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true);

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
                name: "IX_Comments_AuthorId_CreatedAt_PostId",
                table: "Comments",
                columns: new[] { "AuthorId", "CreatedAt", "PostId" });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_PostId_CreatedAt",
                table: "Comments",
                columns: new[] { "PostId", "CreatedAt" });

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
                name: "IX_PostLikes_PostId_CreatedAt",
                table: "PostLikes",
                columns: new[] { "PostId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PostLikes_UserId_CreatedAt_PostId",
                table: "PostLikes",
                columns: new[] { "UserId", "CreatedAt", "PostId" });

            migrationBuilder.CreateIndex(
                name: "IX_Posts_AuthorId_CreatedAt",
                table: "Posts",
                columns: new[] { "AuthorId", "CreatedAt" });

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
                name: "IX_PostTopics_Topic_PostId",
                table: "PostTopics",
                columns: new[] { "Topic", "PostId" });

            migrationBuilder.CreateIndex(
                name: "IX_PostViews_Day",
                table: "PostViews",
                column: "Day");

            migrationBuilder.CreateIndex(
                name: "IX_PostViews_PostId_Day",
                table: "PostViews",
                columns: new[] { "PostId", "Day" });

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
                name: "IX_Uploads_Key",
                table: "Uploads",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Uploads_OwnerId",
                table: "Uploads",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Uploads_PostId_CreatedAt",
                table: "Uploads",
                columns: new[] { "PostId", "CreatedAt" });

            // Full-text search over posts: an external-content FTS5 table kept in sync by triggers. Posts has a
            // UUID (TEXT) primary key, so the index is keyed by the table's implicit rowid. VACUUM keeps rowids of
            // tables with indexes; a migration that rebuilds Posts must run INSERT INTO PostsFts(PostsFts) VALUES ('rebuild').
            migrationBuilder.Sql("""
                CREATE VIRTUAL TABLE PostsFts USING fts5(Title, Body, content='Posts', content_rowid='rowid', tokenize='porter unicode61');
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER Posts_ai AFTER INSERT ON Posts BEGIN
                  INSERT INTO PostsFts(rowid, Title, Body) VALUES (new.rowid, new.Title, new.Body);
                END;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER Posts_ad AFTER DELETE ON Posts BEGIN
                  INSERT INTO PostsFts(PostsFts, rowid, Title, Body) VALUES ('delete', old.rowid, old.Title, old.Body);
                END;
                """);
            // Only re-index when searchable columns change, not on every counter bump.
            migrationBuilder.Sql("""
                CREATE TRIGGER Posts_au AFTER UPDATE OF Title, Body ON Posts BEGIN
                  INSERT INTO PostsFts(PostsFts, rowid, Title, Body) VALUES ('delete', old.rowid, old.Title, old.Body);
                  INSERT INTO PostsFts(rowid, Title, Body) VALUES (new.rowid, new.Title, new.Body);
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS Posts_au;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS Posts_ad;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS Posts_ai;");
            migrationBuilder.Sql("DROP TABLE IF EXISTS PostsFts;");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "Bookmarks");

            migrationBuilder.DropTable(
                name: "CommentLikes");

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
                name: "PostLikes");

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
                name: "Uploads");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "Comments");

            migrationBuilder.DropTable(
                name: "Posts");

            migrationBuilder.DropTable(
                name: "AspNetUsers");
        }
    }
}
