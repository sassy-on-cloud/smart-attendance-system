BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    ALTER TABLE [AttendanceSessions] ADD [CreatedAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    ALTER TABLE [AttendanceSessions] ADD [CreatedByUserId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    ALTER TABLE [AttendanceSessions] ADD [LastModifiedAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    ALTER TABLE [AttendanceSessions] ADD [LastModifiedByUserId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    ALTER TABLE [AttendanceSessions] ADD [State] nvarchar(20) NOT NULL DEFAULT N'Open';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    ALTER TABLE [AttendanceSessions] ADD [Version] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    ALTER TABLE [Attendances] ADD [LastModifiedAtUtc] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    ALTER TABLE [Attendances] ADD [LastModifiedByUserId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    ALTER TABLE [Attendances] ADD [RecordedByUserId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    ALTER TABLE [Attendances] ADD [Version] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    CREATE TABLE [AttendanceHistory] (
        [AttendanceHistoryId] bigint NOT NULL IDENTITY,
        [AttendanceId] int NOT NULL,
        [ActorUserId] int NOT NULL,
        [ChangedAtUtc] datetime2 NOT NULL,
        [ChangeType] nvarchar(20) NOT NULL,
        [PreviousStatus] nvarchar(20) NULL,
        [PreviousMethod] nvarchar(20) NULL,
        [Status] nvarchar(20) NOT NULL,
        [Method] nvarchar(20) NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [Version] int NOT NULL,
        CONSTRAINT [PK_AttendanceHistory] PRIMARY KEY ([AttendanceHistoryId]),
        CONSTRAINT [CK_AttendanceHistory_ChangeType] CHECK ([ChangeType] IN ('Created','Corrected')),
        CONSTRAINT [CK_AttendanceHistory_Reason] CHECK ([Reason] <> ''),
        CONSTRAINT [CK_AttendanceHistory_Version] CHECK ([Version] > 0),
        CONSTRAINT [FK_AttendanceHistory_Attendances_AttendanceId] FOREIGN KEY ([AttendanceId]) REFERENCES [Attendances] ([AttendanceId]),
        CONSTRAINT [FK_AttendanceHistory_Users_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [Users] ([UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    CREATE TABLE [AttendanceSessionHistory] (
        [AttendanceSessionHistoryId] bigint NOT NULL IDENTITY,
        [AttendanceSessionId] int NOT NULL,
        [ActorUserId] int NOT NULL,
        [ChangedAtUtc] datetime2 NOT NULL,
        [PreviousState] nvarchar(20) NULL,
        [State] nvarchar(20) NOT NULL,
        [PreviousStartsAtUtc] datetime2 NULL,
        [PreviousEndsAtUtc] datetime2 NULL,
        [StartsAtUtc] datetime2 NOT NULL,
        [EndsAtUtc] datetime2 NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [Version] int NOT NULL,
        CONSTRAINT [PK_AttendanceSessionHistory] PRIMARY KEY ([AttendanceSessionHistoryId]),
        CONSTRAINT [CK_AttendanceSessionHistory_Version] CHECK ([Version] > 0),
        CONSTRAINT [FK_AttendanceSessionHistory_AttendanceSessions_AttendanceSessionId] FOREIGN KEY ([AttendanceSessionId]) REFERENCES [AttendanceSessions] ([AttendanceSessionId]),
        CONSTRAINT [FK_AttendanceSessionHistory_Users_ActorUserId] FOREIGN KEY ([ActorUserId]) REFERENCES [Users] ([UserId])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    CREATE INDEX [IX_AttendanceSessions_CreatedByUserId] ON [AttendanceSessions] ([CreatedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    CREATE INDEX [IX_AttendanceSessions_LastModifiedByUserId] ON [AttendanceSessions] ([LastModifiedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    EXEC(N'ALTER TABLE [AttendanceSessions] ADD CONSTRAINT [CK_AttendanceSessions_State] CHECK ([State] IN (''Open'',''Closed'',''Cancelled''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    EXEC(N'ALTER TABLE [AttendanceSessions] ADD CONSTRAINT [CK_AttendanceSessions_Version] CHECK ([Version] > 0)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    CREATE INDEX [IX_Attendances_LastModifiedByUserId] ON [Attendances] ([LastModifiedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    CREATE INDEX [IX_Attendances_RecordedByUserId] ON [Attendances] ([RecordedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    EXEC(N'ALTER TABLE [Attendances] ADD CONSTRAINT [CK_Attendances_Version] CHECK ([Version] > 0)');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    CREATE INDEX [IX_AttendanceHistory_ActorUserId] ON [AttendanceHistory] ([ActorUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AttendanceHistory_AttendanceId_Version] ON [AttendanceHistory] ([AttendanceId], [Version]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    CREATE INDEX [IX_AttendanceSessionHistory_ActorUserId] ON [AttendanceSessionHistory] ([ActorUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    CREATE UNIQUE INDEX [IX_AttendanceSessionHistory_AttendanceSessionId_Version] ON [AttendanceSessionHistory] ([AttendanceSessionId], [Version]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    ALTER TABLE [Attendances] ADD CONSTRAINT [FK_Attendances_Users_LastModifiedByUserId] FOREIGN KEY ([LastModifiedByUserId]) REFERENCES [Users] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    ALTER TABLE [Attendances] ADD CONSTRAINT [FK_Attendances_Users_RecordedByUserId] FOREIGN KEY ([RecordedByUserId]) REFERENCES [Users] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    ALTER TABLE [AttendanceSessions] ADD CONSTRAINT [FK_AttendanceSessions_Users_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [Users] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    ALTER TABLE [AttendanceSessions] ADD CONSTRAINT [FK_AttendanceSessions_Users_LastModifiedByUserId] FOREIGN KEY ([LastModifiedByUserId]) REFERENCES [Users] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024065458_AttendanceManagementAudit'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261024065458_AttendanceManagementAudit', N'10.0.11');
END;

COMMIT;
GO

