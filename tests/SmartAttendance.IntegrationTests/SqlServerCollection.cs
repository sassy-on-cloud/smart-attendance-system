using Xunit;

namespace SmartAttendance.IntegrationTests;

[CollectionDefinition("SqlServer", DisableParallelization = true)]
public sealed class SqlServerCollection : ICollectionFixture<SqlServerFixture> { }
