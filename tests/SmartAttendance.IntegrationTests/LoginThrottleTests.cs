using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using SmartAttendance.API.Security;
using Xunit;

namespace SmartAttendance.IntegrationTests;

public sealed class MutableLoginClock : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class LoginThrottleTests
{
    [Fact]
    public async Task Account_limit_normalizes_identity_and_cannot_be_bypassed_by_parallel_guesses()
    {
        using var factory = new SecurityApiFactory(); factory.ResetDatabase();
        var throttle = factory.Services.GetRequiredService<LoginAttemptThrottle>();
        var attempts = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Task.Run(() =>
            throttle.Reserve(EmailIdentity.Normalize(i % 2 == 0 ? "ONE@example.test" : "one@example.test")!))));
        Assert.Equal(5, attempts.Count(r => r.RetryAfterSeconds == 0));
        Assert.Equal(5, attempts.Count(r => r.RetryAfterSeconds > 0));
    }

    [Fact]
    public async Task Account_throttle_expires_and_success_resets_attempts_without_sleeping()
    {
        var clock = new MutableLoginClock();
        using var factory = new SecurityApiFactory { TestClock = clock }; factory.ResetDatabase();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        async Task<HttpResponseMessage> Attempt(string password) => await client.PostAsJsonAsync("/api/users/login", new { email = "one@example.test", password });
        for (var i = 0; i < 4; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await Attempt("incorrect")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Attempt(SecurityApiFactory.Password)).StatusCode);
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await Attempt("incorrect")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Attempt(SecurityApiFactory.Password)).StatusCode);
        clock.Now = clock.Now.AddMinutes(6);
        Assert.Equal(HttpStatusCode.OK, (await Attempt(SecurityApiFactory.Password)).StatusCode);
    }

    [Fact]
    public async Task Ip_limit_returns_429_and_untrusted_forwarded_headers_do_not_change_partition()
    {
        using var factory = new SecurityApiFactory { ConfigurationOverrides = new Dictionary<string, string?>
        {
            ["LoginSecurity:IpPermitLimit"] = "3", ["LoginSecurity:AccountAttemptLimit"] = "20"
        }};
        factory.ResetDatabase(); using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        for (var i = 0; i < 3; i++)
        {
            client.DefaultRequestHeaders.Remove("X-Forwarded-For"); client.DefaultRequestHeaders.Add("X-Forwarded-For", $"192.0.2.{i + 1}");
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/users/login", new { email = $"unknown{i}@example.test", password = "incorrect" })).StatusCode);
        }
        var blocked = await client.PostAsJsonAsync("/api/users/login", new { email = "one@example.test", password = SecurityApiFactory.Password });
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode); Assert.NotNull(blocked.Headers.RetryAfter);
        Assert.True(blocked.Headers.CacheControl!.NoStore);
    }

    [Fact]
    public async Task Unknown_accounts_receive_the_same_account_throttle_as_existing_accounts()
    {
        using var factory = new SecurityApiFactory(); factory.ResetDatabase(); using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        for (var i = 0; i < 5; i++) Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/users/login", new { email = "missing@example.test", password = "incorrect" })).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync("/api/users/login", new { email = "MISSING@example.test", password = "incorrect" })).StatusCode);
    }
}
