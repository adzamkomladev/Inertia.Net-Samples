using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TechLandier.Data.Migrations
{
    /// <inheritdoc />
    public partial class DeliveryDispatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Capacity",
                table: "Riders",
                type: "integer",
                nullable: false,
                defaultValue: 6); // existing riders start at the default capacity

            migrationBuilder.AddColumn<int>(
                name: "Attempts",
                table: "Deliveries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "Deliveries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceivedBy",
                table: "Deliveries",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Capacity",
                table: "Riders");

            migrationBuilder.DropColumn(
                name: "Attempts",
                table: "Deliveries");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "Deliveries");

            migrationBuilder.DropColumn(
                name: "ReceivedBy",
                table: "Deliveries");
        }
    }
}
