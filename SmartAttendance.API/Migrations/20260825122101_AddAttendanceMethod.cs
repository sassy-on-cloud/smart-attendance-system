using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAttendance.API.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendanceMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Method",
                table: "Attendances",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Method",
                table: "Attendances");
        }
    }
}
