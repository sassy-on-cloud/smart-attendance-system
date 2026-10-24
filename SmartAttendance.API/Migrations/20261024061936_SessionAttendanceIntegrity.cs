using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartAttendance.API.Migrations
{
    /// <inheritdoc />
    public partial class SessionAttendanceIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (ActiveProvider != "Microsoft.EntityFrameworkCore.SqlServer")
                throw new NotSupportedException("This preservation migration requires SQL Server.");

            // Fail before touching schema; never silently merge accounts or student profiles.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT [UserId] FROM [Students] GROUP BY [UserId] HAVING COUNT(*) > 1)
                    THROW 51000, 'Duplicate student profiles require reviewed reconciliation before migration.', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Attendances_Courses_CourseId",
                table: "Attendances");

            migrationBuilder.DropForeignKey(
                name: "FK_Attendances_Students_StudentId",
                table: "Attendances");

            migrationBuilder.DropForeignKey(
                name: "FK_Students_Users_UserId",
                table: "Students");

            migrationBuilder.DropIndex(
                name: "IX_Students_UserId",
                table: "Students");

            migrationBuilder.DropIndex(
                name: "IX_Attendances_StudentId",
                table: "Attendances");

            migrationBuilder.AddColumn<int>(
                name: "AttendanceSessionId",
                table: "Attendances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacyMethod",
                table: "Attendances",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacyStatus",
                table: "Attendances",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AttendanceSessions",
                columns: table => new
                {
                    AttendanceSessionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CourseId = table.Column<int>(type: "int", nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EndsAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsLegacy = table.Column<bool>(type: "bit", nullable: false),
                    LegacyAttendanceId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AttendanceSessions", x => x.AttendanceSessionId);
                    table.UniqueConstraint("AK_AttendanceSessions_AttendanceSessionId_CourseId", x => new { x.AttendanceSessionId, x.CourseId });
                    table.CheckConstraint("CK_AttendanceSessions_Meeting", "([IsLegacy] = 1 AND [StartsAtUtc] IS NULL AND [EndsAtUtc] IS NULL AND [LegacyAttendanceId] IS NOT NULL)\nOR ([IsLegacy] = 0 AND [StartsAtUtc] IS NOT NULL AND [EndsAtUtc] IS NOT NULL\n    AND [EndsAtUtc] > [StartsAtUtc] AND [LegacyAttendanceId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_AttendanceSessions_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "CourseId");
                });

            migrationBuilder.CreateTable(
                name: "CourseEnrollments",
                columns: table => new
                {
                    StudentId = table.Column<int>(type: "int", nullable: false),
                    CourseId = table.Column<int>(type: "int", nullable: false),
                    IsLegacyInferred = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseEnrollments", x => new { x.StudentId, x.CourseId });
                    table.ForeignKey(
                        name: "FK_CourseEnrollments_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "CourseId");
                    table.ForeignKey(
                        name: "FK_CourseEnrollments_Students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "Students",
                        principalColumn: "StudentId");
                });

            migrationBuilder.CreateTable(
                name: "TeacherCourseAssignments",
                columns: table => new
                {
                    TeacherUserId = table.Column<int>(type: "int", nullable: false),
                    CourseId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeacherCourseAssignments", x => new { x.TeacherUserId, x.CourseId });
                    table.ForeignKey(
                        name: "FK_TeacherCourseAssignments_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "CourseId");
                    table.ForeignKey(
                        name: "FK_TeacherCourseAssignments_Users_TeacherUserId",
                        column: x => x.TeacherUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            // Each old row gets its own explicitly unverified placeholder. Dates alone cannot
            // reconstruct meetings; do not merge, deduplicate, delete, or invent meeting times.
            // Dynamic batches allow the generated idempotent SQL to compile before new columns exist.
            migrationBuilder.Sql("""
                EXEC(N'
                    INSERT INTO [CourseEnrollments] ([StudentId], [CourseId], [IsLegacyInferred])
                    SELECT DISTINCT [StudentId], [CourseId], 1 FROM [Attendances];

                    SET IDENTITY_INSERT [AttendanceSessions] ON;
                    INSERT INTO [AttendanceSessions]
                        ([AttendanceSessionId], [CourseId], [StartsAtUtc], [EndsAtUtc], [IsLegacy], [LegacyAttendanceId])
                    SELECT [AttendanceId], [CourseId], NULL, NULL, 1, [AttendanceId] FROM [Attendances];
                    SET IDENTITY_INSERT [AttendanceSessions] OFF;

                    UPDATE [Attendances] SET [AttendanceSessionId] = [AttendanceId],
                        [LegacyStatus] = [Status], [LegacyMethod] = [Method],
                        [Status] = CASE UPPER(LTRIM(RTRIM([Status])))
                            WHEN ''PRESENT'' THEN ''Present'' WHEN ''ABSENT'' THEN ''Absent''
                            WHEN ''LATE'' THEN ''Late'' WHEN ''EXCUSED'' THEN ''Excused'' ELSE ''Unverified'' END,
                        [Method] = CASE UPPER(LTRIM(RTRIM([Method])))
                            WHEN ''MANUAL'' THEN ''Manual'' WHEN ''FACERECOGNITION'' THEN ''FaceRecognition''
                            ELSE ''LegacyUnknown'' END;
                ');
                """);

            migrationBuilder.AlterColumn<int>(
                name: "AttendanceSessionId", table: "Attendances", type: "int", nullable: false,
                oldClrType: typeof(int), oldType: "int", oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "Attendances",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Method",
                table: "Attendances",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_Students_UserId",
                table: "Students",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attendances_AttendanceSessionId_CourseId",
                table: "Attendances",
                columns: new[] { "AttendanceSessionId", "CourseId" });

            migrationBuilder.CreateIndex(
                name: "IX_Attendances_StudentId_AttendanceSessionId",
                table: "Attendances",
                columns: new[] { "StudentId", "AttendanceSessionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Attendances_StudentId_CourseId",
                table: "Attendances",
                columns: new[] { "StudentId", "CourseId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Attendances_Method",
                table: "Attendances",
                sql: "[Method] IN ('Manual','FaceRecognition','LegacyUnknown')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Attendances_Status",
                table: "Attendances",
                sql: "[Status] IN ('Present','Absent','Late','Excused','Unverified')");

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSessions_CourseId_StartsAtUtc",
                table: "AttendanceSessions",
                columns: new[] { "CourseId", "StartsAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AttendanceSessions_LegacyAttendanceId",
                table: "AttendanceSessions",
                column: "LegacyAttendanceId",
                unique: true,
                filter: "[LegacyAttendanceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CourseEnrollments_CourseId",
                table: "CourseEnrollments",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_TeacherCourseAssignments_CourseId",
                table: "TeacherCourseAssignments",
                column: "CourseId");

            migrationBuilder.AddForeignKey(
                name: "FK_Attendances_AttendanceSessions_AttendanceSessionId_CourseId",
                table: "Attendances",
                columns: new[] { "AttendanceSessionId", "CourseId" },
                principalTable: "AttendanceSessions",
                principalColumns: new[] { "AttendanceSessionId", "CourseId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Attendances_CourseEnrollments_StudentId_CourseId",
                table: "Attendances",
                columns: new[] { "StudentId", "CourseId" },
                principalTable: "CourseEnrollments",
                principalColumns: new[] { "StudentId", "CourseId" });

            migrationBuilder.AddForeignKey(
                name: "FK_Attendances_Courses_CourseId",
                table: "Attendances",
                column: "CourseId",
                principalTable: "Courses",
                principalColumn: "CourseId");

            migrationBuilder.AddForeignKey(
                name: "FK_Attendances_Students_StudentId",
                table: "Attendances",
                column: "StudentId",
                principalTable: "Students",
                principalColumn: "StudentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Students_Users_UserId",
                table: "Students",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "Automatic rollback would discard session/enrollment history. Restore a reviewed backup or use an approved forward migration.");
        }
    }
}
