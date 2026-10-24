BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    IF EXISTS (SELECT [UserId] FROM [Students] GROUP BY [UserId] HAVING COUNT(*) > 1)
        THROW 51000, 'Duplicate student profiles require reviewed reconciliation before migration.', 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    ALTER TABLE [Attendances] DROP CONSTRAINT [FK_Attendances_Courses_CourseId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    ALTER TABLE [Attendances] DROP CONSTRAINT [FK_Attendances_Students_StudentId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    ALTER TABLE [Students] DROP CONSTRAINT [FK_Students_Users_UserId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    DROP INDEX [IX_Students_UserId] ON [Students];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    DROP INDEX [IX_Attendances_StudentId] ON [Attendances];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    ALTER TABLE [Attendances] ADD [AttendanceSessionId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    ALTER TABLE [Attendances] ADD [LegacyMethod] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    ALTER TABLE [Attendances] ADD [LegacyStatus] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    CREATE TABLE [AttendanceSessions] (
        [AttendanceSessionId] int NOT NULL IDENTITY,
        [CourseId] int NOT NULL,
        [StartsAtUtc] datetime2 NULL,
        [EndsAtUtc] datetime2 NULL,
        [IsLegacy] bit NOT NULL,
        [LegacyAttendanceId] int NULL,
        CONSTRAINT [PK_AttendanceSessions] PRIMARY KEY ([AttendanceSessionId]),
        CONSTRAINT [AK_AttendanceSessions_AttendanceSessionId_CourseId] UNIQUE ([AttendanceSessionId], [CourseId]),
        CONSTRAINT [CK_AttendanceSessions_Meeting] CHECK (([IsLegacy] = 1 AND [StartsAtUtc] IS NULL AND [EndsAtUtc] IS NULL AND [LegacyAttendanceId] IS NOT NULL)
    OR ([IsLegacy] = 0 AND [StartsAtUtc] IS NOT NULL AND [EndsAtUtc] IS NOT NULL
        AND [EndsAtUtc] > [StartsAtUtc] AND [LegacyAttendanceId] IS NULL)),
        CONSTRAINT [FK_AttendanceSessions_Courses_CourseId] FOREIGN KEY ([CourseId]) REFERENCES [Courses] ([CourseId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    CREATE TABLE [CourseEnrollments] (
        [StudentId] int NOT NULL,
        [CourseId] int NOT NULL,
        [IsLegacyInferred] bit NOT NULL,
        CONSTRAINT [PK_CourseEnrollments] PRIMARY KEY ([StudentId], [CourseId]),
        CONSTRAINT [FK_CourseEnrollments_Courses_CourseId] FOREIGN KEY ([CourseId]) REFERENCES [Courses] ([CourseId]),
        CONSTRAINT [FK_CourseEnrollments_Students_StudentId] FOREIGN KEY ([StudentId]) REFERENCES [Students] ([StudentId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    CREATE TABLE [TeacherCourseAssignments] (
        [TeacherUserId] int NOT NULL,
        [CourseId] int NOT NULL,
        CONSTRAINT [PK_TeacherCourseAssignments] PRIMARY KEY ([TeacherUserId], [CourseId]),
        CONSTRAINT [FK_TeacherCourseAssignments_Courses_CourseId] FOREIGN KEY ([CourseId]) REFERENCES [Courses] ([CourseId]),
        CONSTRAINT [FK_TeacherCourseAssignments_Users_TeacherUserId] FOREIGN KEY ([TeacherUserId]) REFERENCES [Users] ([UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
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
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Attendances]') AND [c].[name] = N'AttendanceSessionId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Attendances] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Attendances] ALTER COLUMN [AttendanceSessionId] int NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Attendances]') AND [c].[name] = N'Status');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Attendances] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [Attendances] ALTER COLUMN [Status] nvarchar(20) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Attendances]') AND [c].[name] = N'Method');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Attendances] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [Attendances] ALTER COLUMN [Method] nvarchar(20) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Students_UserId] ON [Students] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    CREATE INDEX [IX_Attendances_AttendanceSessionId_CourseId] ON [Attendances] ([AttendanceSessionId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Attendances_StudentId_AttendanceSessionId] ON [Attendances] ([StudentId], [AttendanceSessionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    CREATE INDEX [IX_Attendances_StudentId_CourseId] ON [Attendances] ([StudentId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    EXEC(N'ALTER TABLE [Attendances] ADD CONSTRAINT [CK_Attendances_Method] CHECK ([Method] IN (''Manual'',''FaceRecognition'',''LegacyUnknown''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    EXEC(N'ALTER TABLE [Attendances] ADD CONSTRAINT [CK_Attendances_Status] CHECK ([Status] IN (''Present'',''Absent'',''Late'',''Excused'',''Unverified''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    CREATE INDEX [IX_AttendanceSessions_CourseId_StartsAtUtc] ON [AttendanceSessions] ([CourseId], [StartsAtUtc]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_AttendanceSessions_LegacyAttendanceId] ON [AttendanceSessions] ([LegacyAttendanceId]) WHERE [LegacyAttendanceId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    CREATE INDEX [IX_CourseEnrollments_CourseId] ON [CourseEnrollments] ([CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    CREATE INDEX [IX_TeacherCourseAssignments_CourseId] ON [TeacherCourseAssignments] ([CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    ALTER TABLE [Attendances] ADD CONSTRAINT [FK_Attendances_AttendanceSessions_AttendanceSessionId_CourseId] FOREIGN KEY ([AttendanceSessionId], [CourseId]) REFERENCES [AttendanceSessions] ([AttendanceSessionId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    ALTER TABLE [Attendances] ADD CONSTRAINT [FK_Attendances_CourseEnrollments_StudentId_CourseId] FOREIGN KEY ([StudentId], [CourseId]) REFERENCES [CourseEnrollments] ([StudentId], [CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    ALTER TABLE [Attendances] ADD CONSTRAINT [FK_Attendances_Courses_CourseId] FOREIGN KEY ([CourseId]) REFERENCES [Courses] ([CourseId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    ALTER TABLE [Attendances] ADD CONSTRAINT [FK_Attendances_Students_StudentId] FOREIGN KEY ([StudentId]) REFERENCES [Students] ([StudentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    ALTER TABLE [Students] ADD CONSTRAINT [FK_Students_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024061936_SessionAttendanceIntegrity'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261024061936_SessionAttendanceIntegrity', N'10.0.11');
END;

COMMIT;
GO

