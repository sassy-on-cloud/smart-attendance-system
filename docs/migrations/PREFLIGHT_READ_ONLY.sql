-- Read-only checks for the intended SQL Server database; review before running.
-- These were NOT executed against the user's database during Milestone 3.
SELECT [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId];
SELECT 'Users' AS [Entity], COUNT_BIG(*) AS [RowCount] FROM [Users]
UNION ALL SELECT 'Students', COUNT_BIG(*) FROM [Students]
UNION ALL SELECT 'Courses', COUNT_BIG(*) FROM [Courses]
UNION ALL SELECT 'Attendances', COUNT_BIG(*) FROM [Attendances];

-- Must be zero before the migration; reconcile explicitly, never auto-delete/merge.
SELECT COUNT_BIG(*) AS [DuplicateStudentAccountGroups]
FROM (SELECT [UserId] FROM [Students] GROUP BY [UserId] HAVING COUNT_BIG(*) > 1) AS d;

SELECT COUNT_BIG(*) AS [OrphanStudentUsers]
FROM [Students] AS s LEFT JOIN [Users] AS u ON s.[UserId]=u.[UserId] WHERE u.[UserId] IS NULL;
SELECT COUNT_BIG(*) AS [OrphanAttendanceStudents]
FROM [Attendances] AS a LEFT JOIN [Students] AS s ON a.[StudentId]=s.[StudentId] WHERE s.[StudentId] IS NULL;
SELECT COUNT_BIG(*) AS [OrphanAttendanceCourses]
FROM [Attendances] AS a LEFT JOIN [Courses] AS c ON a.[CourseId]=c.[CourseId] WHERE c.[CourseId] IS NULL;

-- Informational compatibility issues needing review; these do not trigger automatic data cleanup.
SELECT COUNT_BIG(*) AS [StudentProfilesOnNonStudentAccounts]
FROM [Students] AS s INNER JOIN [Users] AS u ON s.[UserId]=u.[UserId] WHERE u.[Role] <> 'Student';
SELECT COUNT_BIG(*) AS [DuplicateEmailGroups]
FROM (SELECT [Email] FROM [Users] GROUP BY [Email] HAVING COUNT_BIG(*) > 1) AS d;
SELECT COUNT_BIG(*) AS [HistoricalAttendanceRows] FROM [Attendances];
