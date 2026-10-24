using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Data;
using Xunit;

namespace SmartAttendance.IntegrationTests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SMARTATTENDANCE_RUN_SQLSERVER_TESTS") != "1")
            Skip = "Opt in to a private disposable SQL Server container with SMARTATTENDANCE_RUN_SQLSERVER_TESTS=1.";
    }
}

// Never accepts a connection string from the host or reads application configuration.
public sealed class SqlServerFixture : IAsyncLifetime
{
    private string? _containerId;
    private string? _server;
    private readonly string _password = "Test!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("SMARTATTENDANCE_RUN_SQLSERVER_TESTS") != "1") return;
        try
        {
            _containerId = (await Docker("run", "--detach", "--rm", "--pull=never",
                "--name", "smartattendance-tests-" + Guid.NewGuid().ToString("N"),
                "--label", "smartattendance.disposable-test=true", "--memory=2g",
                "--publish", "127.0.0.1::1433", "--env", "ACCEPT_EULA=Y", "--env", "MSSQL_PID=Developer",
                "--env", "MSSQL_MEMORY_LIMIT_MB=1024", "--env", "MSSQL_SA_PASSWORD",
                "mcr.microsoft.com/mssql/server:2022-latest")).Trim();
            var port = (await Docker("port", _containerId, "1433/tcp")).Trim();
            if (!port.StartsWith("127.0.0.1:") || !int.TryParse(port.Split(':')[1], out var portNumber))
                throw new InvalidOperationException("Test container must publish on loopback only.");
            _server = $"127.0.0.1,{portNumber}";
            for (var attempt = 0; attempt < 60; attempt++)
            {
                try
                {
                    await using var connection = new SqlConnection(ConnectionString("master"));
                    await connection.OpenAsync();
                    return;
                }
                catch (SqlException) { await Task.Delay(1000); }
            }
            throw new InvalidOperationException("The disposable SQL Server did not become ready.");
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public AppDbContext NewDatabase()
    {
        if (_containerId is null || _server is null)
            throw new InvalidOperationException("A private test container is required; no external database fallback.");
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString("AttendanceTests_" + Guid.NewGuid().ToString("N"))).Options);
    }

    private string ConnectionString(string database) => new SqlConnectionStringBuilder
    {
        DataSource = _server, InitialCatalog = database, UserID = "sa", Password = _password,
        TrustServerCertificate = true, Encrypt = true, ConnectTimeout = 1, Pooling = false
    }.ConnectionString;

    private async Task<string> Docker(params string[] arguments)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        // Secret is inherited by Docker, never placed in CLI arguments or printed.
        start.Environment["MSSQL_SA_PASSWORD"] = _password;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Docker.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException("Disposable test container command failed: " + error.Replace(_password, "[redacted]"));
        return output;
    }

    public async Task DisposeAsync()
    {
        if (_containerId is not null)
        {
            // Only the container created by this fixture is removed; never prune or enumerate user containers.
            await Docker("rm", "--force", "--volumes", _containerId);
            _containerId = null;
        }
    }
}
