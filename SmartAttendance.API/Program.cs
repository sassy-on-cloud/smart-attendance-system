using Microsoft.EntityFrameworkCore;
using SmartAttendance.API.Data;
using SmartAttendance.API.Models;
using SmartAttendance.API.Services;
using Microsoft.OpenApi;
using SmartAttendance.API.Reporting;
using SmartAttendance.API.Security;
using SmartAttendance.API.Recognition;

var provisionAdmin = args.Contains("--provision-admin", StringComparer.Ordinal);
var builder = WebApplication.CreateBuilder(args.Where(arg => arg != "--provision-admin").ToArray());

builder.Services.AddControllers();

builder.Services.AddAttendanceAuthentication();
builder.Services.AddScoped<AccountService>();
builder.Services.AddScoped<InitialAdminProvisioner>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<AttendanceAccess>();
builder.Services.AddScoped<AttendanceSessionService>();
builder.Services.AddScoped<AttendanceReportService>();
builder.Services.AddSingleton<IRecognitionComponent, UnavailableRecognitionComponent>();
builder.Services.AddScoped<RecognitionWorkflow>();

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")
    )
);

// Swagger
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.OperationFilter<AttendanceReportOperationFilter>();
    options.OperationFilter<AuthenticationOperationFilter>();
    options.OperationFilter<RecognitionOperationFilter>();
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token."
    });

    options.AddSecurityRequirement(document =>
        new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = []
        });
});

var app = builder.Build();

if (provisionAdmin)
{
    // Explicit operator-only command: no HTTP endpoint, automatic seeding or automatic migration.
    var settings = builder.Configuration.GetSection("InitialAdmin").Get<InitialAdminSettings>() ?? new();
    try
    {
        await using var scope = app.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<InitialAdminProvisioner>().ProvisionAsync(settings, CancellationToken.None);
        Console.WriteLine(result switch
        {
            ProvisioningOutcome.Created => "Initial Admin created. Remove the one-time provisioning secrets.",
            ProvisioningOutcome.AlreadyProvisioned => "An Admin already exists. No account was changed.",
            ProvisioningOutcome.EmailConflict => "Provisioning email is already in use. No account was promoted.",
            _ => "Provide valid InitialAdmin:Name, Email and Password through secure configuration."
        });
        Environment.ExitCode = result == ProvisioningOutcome.Created ? 0 : 2;
    }
    catch
    {
        Console.Error.WriteLine("Provisioning failed. Verify approved schema and configuration; no migration is run by this command.");
        Environment.ExitCode = 1;
    }
    finally { settings.Password = string.Empty; await app.DisposeAsync(); }
    return;
}

// Never send exception messages, configuration values or database details to clients.
app.UseExceptionHandler(errorApp => errorApp.Run(context =>
    Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
        title: "An unexpected error occurred.").ExecuteAsync(context)));

// Swagger
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseRouting();
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposes the entry point to the isolated HTTP integration test host.
public partial class Program { }
