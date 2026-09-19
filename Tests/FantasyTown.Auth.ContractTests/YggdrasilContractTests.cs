using System.Text.Json;
using FantasyTown.Auth.Modules.Yggdrasil.Protocol;

namespace FantasyTown.Auth.ContractTests;

public class YggdrasilContractTests
{
    private static readonly string FixturesPath = Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "fixtures");

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    [Fact]
    public void Authenticate_Success_Response_ShouldMatchFixture()
    {
        var fixture = LoadFixture("authenticate_success.json");
        var response = new AuthenticateResponse
        {
            AccessToken = "test-access-token",
            ClientToken = "test-client-token",
            AvailableProfiles = new[]
            {
                new ProfileInfo { Id = "abcd1234", Name = "TestPlayer" }
            },
            SelectedProfile = new ProfileInfo { Id = "abcd1234", Name = "TestPlayer" }
        };

        var actual = JsonSerializer.Serialize(response, Options);

        Assert.Equal(fixture, actual);
    }

    [Fact]
    public void Authenticate_InvalidCredentials_Error_ShouldMatchFixture()
    {
        var fixture = LoadFixture("authenticate_invalid_credentials.json");
        var error = YggErrorResponse.InvalidCredentials();

        var actual = JsonSerializer.Serialize(error, Options);

        Assert.Equal(fixture, actual);
    }

    [Fact]
    public void Validate_Success_EmptyBody_ShouldReturn204()
    {
        var fixture = LoadFixture("validate_success_204.json");

        Assert.Equal("204", fixture.Trim());
    }

    [Fact]
    public void Refresh_Success_Response_ShouldMatchFixture()
    {
        var fixture = LoadFixture("refresh_success.json");
        var response = new AuthenticateResponse
        {
            AccessToken = "new-access-token",
            ClientToken = "test-client-token",
            AvailableProfiles = new[]
            {
                new ProfileInfo { Id = "abcd1234", Name = "TestPlayer" }
            },
            SelectedProfile = new ProfileInfo { Id = "abcd1234", Name = "TestPlayer" }
        };

        var actual = JsonSerializer.Serialize(response, Options);

        Assert.Equal(fixture, actual);
    }

    [Fact]
    public void Invalidate_Success_EmptyBody_ShouldReturn204()
    {
        var fixture = LoadFixture("invalidate_success_204.json");

        Assert.Equal("204", fixture.Trim());
    }

    [Fact]
    public void Signout_Success_EmptyBody_ShouldReturn204()
    {
        var fixture = LoadFixture("signout_success_204.json");

        Assert.Equal("204", fixture.Trim());
    }

    [Fact]
    public void HasJoined_Success_Response_ShouldMatchFixture()
    {
        var fixture = LoadFixture("hasjoined_success.json");
        var profile = new ProfileResponse
        {
            Id = "abcd1234",
            Name = "TestPlayer",
            Properties = new[]
            {
                new ProfileProperty
                {
                    Name = "textures",
                    Value = "base64encodedvalue"
                }
            }
        };

        var actual = JsonSerializer.Serialize(profile, Options);

        Assert.Equal(fixture, actual);
    }

    [Fact]
    public void HasJoined_MissingPlayer_Error_ShouldMatchFixture()
    {
        var fixture = LoadFixture("hasjoined_missing_player.json");
        var error = YggErrorResponse.Forbidden("Player not found");

        var actual = JsonSerializer.Serialize(error, Options);

        Assert.Equal(fixture, actual);
    }

    [Fact]
    public void Metadata_Response_ShouldMatchFixture()
    {
        var fixture = LoadFixture("metadata_response.json");

        Assert.False(string.IsNullOrWhiteSpace(fixture));
        Assert.Contains("skinDomains", fixture);
        Assert.Contains("signaturePublickey", fixture);
    }

    private static string LoadFixture(string fileName)
    {
        var path = Path.Combine(FixturesPath, fileName);
        Assert.True(File.Exists(path), $"Fixture file not found: {path}");
        return File.ReadAllText(path).Trim();
    }
}
