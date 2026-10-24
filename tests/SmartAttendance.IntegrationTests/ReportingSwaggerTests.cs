using System.Net;
using System.Text.Json;
using Xunit;

namespace SmartAttendance.IntegrationTests;

public sealed class ReportingSwaggerTests
{
    [Fact]
    public async Task Swagger_documents_all_reporting_endpoints_policy_filters_and_responses()
    {
        using var factory = new SecurityApiFactory { EnvironmentName = "Development" };
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var response = await client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain(factory.SigningKey, text);
        using var json = JsonDocument.Parse(text);
        var routes = json.RootElement.GetProperty("paths").EnumerateObject().Where(p => p.Name.StartsWith("/api/reports/")).ToArray();
        Assert.Equal(10, routes.Length);
        foreach (var path in routes)
        {
            var operation = path.Value.GetProperty("get");
            Assert.False(string.IsNullOrWhiteSpace(operation.GetProperty("summary").GetString()));
            var description = operation.GetProperty("description").GetString()!;
            Assert.Contains("closed-past-v1", description); Assert.Contains("missing marks", description);
            Assert.Contains("Excused", description); Assert.Contains("legacy", description);
            foreach (var status in new[] { "200", "400", "401", "403", "404" }) Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _), $"{path.Name} missing documented response {status}");
        }
        var parameters = json.RootElement.GetProperty("paths").GetProperty("/api/reports/attendance").GetProperty("get").GetProperty("parameters").EnumerateArray().ToArray();
        Assert.Contains(parameters, p => p.GetProperty("name").GetString() == "fromDate" && p.GetProperty("description").GetString()!.Contains("UTC"));
        var size = parameters.Single(p => p.GetProperty("name").GetString() == "pageSize"); Assert.Equal(100, size.GetProperty("schema").GetProperty("maximum").GetInt32());
    }
}
