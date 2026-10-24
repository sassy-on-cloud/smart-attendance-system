using Microsoft.Extensions.Options;
using Xunit;

namespace SmartAttendance.IntegrationTests;

public sealed class AuthenticationConfigurationTests
{
    [Theory]
    [InlineData("Jwt:Key", "")]
    [InlineData("Jwt:Key", "short")]
    [InlineData("Jwt:Key", "CHANGE_THIS_PLACEHOLDER_SIGNING_KEY_BEFORE_USE")]
    [InlineData("Jwt:Key", "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx")]
    [InlineData("Jwt:Issuer", "")]
    [InlineData("Jwt:Audience", "")]
    [InlineData("Jwt:AccessTokenMinutes", "0")]
    [InlineData("Jwt:ClockSkewSeconds", "300")]
    [InlineData("LoginSecurity:IpPermitLimit", "0")]
    public void Unsafe_configuration_fails_at_start_without_echoing_secrets(string key, string value)
    {
        using var factory = new SecurityApiFactory { ConfigurationOverrides = new Dictionary<string, string?> { [key] = value } };
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.True(error is OptionsValidationException || error.GetBaseException() is OptionsValidationException, error.GetType().Name);
        Assert.DoesNotContain(factory.SigningKey, error.ToString());
        if (key == "Jwt:Key" && value.Length > 0) Assert.DoesNotContain(value, error.ToString());
    }
}
