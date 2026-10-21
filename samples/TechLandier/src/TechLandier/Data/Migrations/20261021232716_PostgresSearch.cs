using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace TechLandier.Data.Migrations
{
    /// <inheritdoc />
    public partial class PostgresSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SearchIndexStates");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_UserId_CreatedAt_Id",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Products_Price",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_Status_CreatedAt_Id",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_Stock_LowStockThreshold",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Orders_Status",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_Installments_DueDate",
                table: "Installments");

            migrationBuilder.DropColumn(
                name: "IndexVersion",
                table: "Products");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "Products",
                type: "tsvector",
                nullable: false)
                .Annotation("Npgsql:TsVectorConfig", "english")
                .Annotation("Npgsql:TsVectorProperties", new[] { "Name", "Brand", "Keywords", "Summary" });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_UserId_UpdatedAt_Id",
                table: "Tickets",
                columns: new[] { "UserId", "UpdatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Name",
                table: "Products",
                column: "Name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_SearchVector",
                table: "Products",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Status_PublishedAt_Id",
                table: "Products",
                columns: new[] { "Status", "PublishedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CreatedAt_Id",
                table: "Orders",
                columns: new[] { "CreatedAt", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tickets_UserId_UpdatedAt_Id",
                table: "Tickets");

            migrationBuilder.DropIndex(
                name: "IX_Products_Name",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_SearchVector",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_Status_PublishedAt_Id",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Orders_CreatedAt_Id",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "Products");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.AddColumn<long>(
                name: "IndexVersion",
                table: "Products",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "SearchIndexStates",
                columns: table => new
                {
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppliedVersion = table.Column<long>(type: "bigint", nullable: false),
                    LeaseToken = table.Column<string>(type: "text", nullable: true),
                    LockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequestedVersion = table.Column<long>(type: "bigint", nullable: false),
                    TaskId = table.Column<long>(type: "bigint", nullable: true),
                    TaskVersion = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchIndexStates", x => x.ProductId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_UserId_CreatedAt_Id",
                table: "Tickets",
                columns: new[] { "UserId", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Price",
                table: "Products",
                column: "Price");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Status_CreatedAt_Id",
                table: "Products",
                columns: new[] { "Status", "CreatedAt", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_Stock_LowStockThreshold",
                table: "Products",
                columns: new[] { "Stock", "LowStockThreshold" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Status",
                table: "Orders",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Installments_DueDate",
                table: "Installments",
                column: "DueDate");

            migrationBuilder.CreateIndex(
                name: "IX_SearchIndexStates_LockedUntil",
                table: "SearchIndexStates",
                column: "LockedUntil");

            migrationBuilder.CreateIndex(
                name: "IX_SearchIndexStates_ProductId",
                table: "SearchIndexStates",
                column: "ProductId",
                filter: "\"RequestedVersion\" > \"AppliedVersion\"");
        }
    }
}
