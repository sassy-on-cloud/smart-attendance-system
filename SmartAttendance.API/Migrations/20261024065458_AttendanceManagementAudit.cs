using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAttendance.API.Migrations
{
    /// <inheritdoc />
    public partial class AttendanceManagementAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "AttendanceSessions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "AttendanceSessions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModifiedAtUtc",
                table: "AttendanceSessions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastModifiedByUserId",
                table: "AttendanceSessions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "State",
                table: "AttendanceSessions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Open");

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "AttendanceSessions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastModifiedAtUtc",
                table: "Attendances",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LastModifiedByUserId",
                table: "Attendances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecordedByUserId",
                table: "Attendances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Attendances",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "AttendanceHistory",
                columns: table => new
                {
                    AttendanceHistoryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttendanceId = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PreviousStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    PreviousMethod = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceHistory", x => x.AttendanceHistoryId);
                    table.CheckConstraint("CK_AttendanceHistory_ChangeType", "[ChangeType] IN ('Created','Corrected')");
                    table.CheckConstraint("CK_AttendanceHistory_Reason", "[Reason] <> ''");
                    table.CheckConstraint("CK_AttendanceHistory_Version", "[Version] > 0");
                    table.ForeignKey(
                        name: "FK_AttendanceHistory_Attendances_AttendanceId",
                        column: x => x.AttendanceId,
                        principalTable: "Attendances",
                        principalColumn: "AttendanceId");
                    table.ForeignKey(
                        name: "FK_AttendanceHistory_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "AttendanceSessionHistory",
                columns: table => new
                {
                    AttendanceSessionHistoryId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttendanceSessionId = table.Column<int>(type: "int", nullable: false),
                    ActorUserId = table.Column<int>(type: "int", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PreviousState = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    State = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PreviousStartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PreviousEndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceSessionHistory", x => x.AttendanceSessionHistoryId);
                    table.CheckConstraint("CK_AttendanceSessionHistory_Version", "[Version] > 0");
                    table.ForeignKey(
                        name: "FK_AttendanceSessionHistory_AttendanceSessions_AttendanceSessionId",
                        column: x => x.AttendanceSessionId,
                        principalTable: "AttendanceSessions",
                        principalColumn: "AttendanceSessionId");
                    table.ForeignKey(
                        name: "FK_AttendanceSessionHistory_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSessions_CreatedByUserId",
                table: "AttendanceSessions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSessions_LastModifiedByUserId",
                table: "AttendanceSessions",
                column: "LastModifiedByUserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AttendanceSessions_State",
                table: "AttendanceSessions",
                sql: "[State] IN ('Open','Closed','Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AttendanceSessions_Version",
                table: "AttendanceSessions",
                sql: "[Version] > 0");

            migrationBuilder.CreateIndex(
                name: "IX_Attendances_LastModifiedByUserId",
                table: "Attendances",
                column: "LastModifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Attendances_RecordedByUserId",
                table: "Attendances",
                column: "RecordedByUserId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Attendances_Version",
                table: "Attendances",
                sql: "[Version] > 0");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceHistory_ActorUserId",
                table: "AttendanceHistory",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceHistory_AttendanceId_Version",
                table: "AttendanceHistory",
                columns: new[] { "AttendanceId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSessionHistory_ActorUserId",
                table: "AttendanceSessionHistory",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSessionHistory_AttendanceSessionId_Version",
                table: "AttendanceSessionHistory",
                columns: new[] { "AttendanceSessionId", "Version" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Attendances_Users_LastModifiedByUserId",
                table: "Attendances",
                column: "LastModifiedByUserId",
                principalTable: "Users",
                principalColumn: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Attendances_Users_RecordedByUserId",
                table: "Attendances",
                column: "RecordedByUserId",
                principalTable: "Users",
                principalColumn: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceSessions_Users_CreatedByUserId",
                table: "AttendanceSessions",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_AttendanceSessions_Users_LastModifiedByUserId",
                table: "AttendanceSessions",
                column: "LastModifiedByUserId",
                principalTable: "Users",
                principalColumn: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Automatic rollback would discard actor and correction history. Use an approved recovery plan or forward migration.");
        }
    }
}
