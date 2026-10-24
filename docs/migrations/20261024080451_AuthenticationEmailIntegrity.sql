BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024080451_AuthenticationEmailIntegrity'
)
BEGIN
    IF EXISTS (SELECT 1 FROM [Users] WHERE DATALENGTH(UPPER(LTRIM(RTRIM([Email])))) > 508)
        THROW 51020, 'An email identity exceeds the normalized key size. Review data before migration.', 1;
    IF EXISTS (
        SELECT UPPER(LTRIM(RTRIM([Email]))) COLLATE Latin1_General_100_CI_AS
        FROM [Users] WHERE LTRIM(RTRIM([Email])) <> ''
        GROUP BY UPPER(LTRIM(RTRIM([Email]))) COLLATE Latin1_General_100_CI_AS HAVING COUNT(*) > 1)
        THROW 51021, 'Duplicate email identities require reviewed reconciliation before migration.', 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024080451_AuthenticationEmailIntegrity'
)
BEGIN
    ALTER TABLE [Users] ADD [NormalizedEmail] nvarchar(254) COLLATE Latin1_General_100_CI_AS NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024080451_AuthenticationEmailIntegrity'
)
BEGIN
    EXEC(N'UPDATE [Users] SET [NormalizedEmail] =
        CASE WHEN LTRIM(RTRIM([Email])) = '''' THEN NULL
        ELSE CONVERT(nvarchar(254), UPPER(LTRIM(RTRIM([Email])))) END;');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024080451_AuthenticationEmailIntegrity'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Users_NormalizedEmail] ON [Users] ([NormalizedEmail]) WHERE [NormalizedEmail] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024080451_AuthenticationEmailIntegrity'
)
BEGIN
    EXEC(N'ALTER TABLE [Users] ADD CONSTRAINT [CK_Users_NormalizedEmail] CHECK (([NormalizedEmail] IS NULL AND LTRIM(RTRIM([Email])) = '''') OR ([NormalizedEmail] IS NOT NULL AND LTRIM(RTRIM([Email])) <> '''' AND [NormalizedEmail] <> ''''))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024080451_AuthenticationEmailIntegrity'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261024080451_AuthenticationEmailIntegrity', N'10.0.11');
END;

COMMIT;
GO

