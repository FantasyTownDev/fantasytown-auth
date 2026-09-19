using System.Text.Json;
using FantasyTown.Auth.Modules.Yggdrasil.Protocol;

namespace FantasyTown.Auth.UnitTests;

public class YggdrasilDtoTests
{
    private static readonly JsonSerializerOptions IndentedOptions = new()
    {
        WriteIndented = true
    };

    [Fact]
    public void AuthenticateRequest_ShouldSerializeCorrectly()
    {
        var request = new AuthenticateRequest
        {
            Agent = new AgentInfo { Name = "Minecraft", Version = 1 },
            Username = "test@example.com",
            Password = "secret",
            ClientToken = "test-client-token",
            RequestUser = true
        };

        var json = JsonSerializer.Serialize(request, IndentedOptions);

        Assert.Contains("\"username\": \"test@example.com\"", json);
        Assert.Contains("\"password\": \"secret\"", json);
        Assert.Contains("\"clientToken\": \"test-client-token\"", json);
        Assert.Contains("\"requestUser\": true", json);
        Assert.Contains("\"name\": \"Minecraft\"", json);
        Assert.Contains("\"version\": 1", json);
    }

    [Fact]
    public void AuthenticateRequest_ShouldDeserializeCorrectly()
    {
        var json = """
        {
            "agent": { "name": "Minecraft", "version": 1 },
            "username": "test@example.com",
            "password": "secret",
            "clientToken": "test-client-token",
            "requestUser": true
        }
        """;

        var request = JsonSerializer.Deserialize<AuthenticateRequest>(json);

        Assert.NotNull(request);
        Assert.Equal("test@example.com", request.Username);
        Assert.Equal("secret", request.Password);
        Assert.Equal("test-client-token", request.ClientToken);
        Assert.True(request.RequestUser);
        Assert.NotNull(request.Agent);
        Assert.Equal("Minecraft", request.Agent.Name);
    }

    [Fact]
    public void AuthenticateRequest_MinimalFields_ShouldDeserialize()
    {
        var json = """
        {
            "username": "player",
            "password": "pass"
        }
        """;

        var request = JsonSerializer.Deserialize<AuthenticateRequest>(json);

        Assert.NotNull(request);
        Assert.Equal("player", request.Username);
        Assert.Null(request.Agent);
        Assert.Null(request.ClientToken);
        Assert.False(request.RequestUser);
    }

    [Fact]
    public void AuthenticateResponse_ShouldSerializeCorrectly()
    {
        var response = new AuthenticateResponse
        {
            AccessToken = "access-token-123",
            ClientToken = "client-token-456",
            AvailableProfiles = new[]
            {
                new ProfileInfo { Id = "abcd1234", Name = "Player1" }
            },
            SelectedProfile = new ProfileInfo { Id = "abcd1234", Name = "Player1" }
        };

        var json = JsonSerializer.Serialize(response, IndentedOptions);

        Assert.Contains("\"accessToken\": \"access-token-123\"", json);
        Assert.Contains("\"clientToken\": \"client-token-456\"", json);
        Assert.Contains("\"id\": \"abcd1234\"", json);
        Assert.Contains("\"name\": \"Player1\"", json);
    }

    [Fact]
    public void ValidateRequest_ShouldSerializeCorrectly()
    {
        var request = new ValidateRequest
        {
            AccessToken = "some-token",
            ClientToken = "some-client"
        };

        var json = JsonSerializer.Serialize(request, IndentedOptions);

        Assert.Contains("\"accessToken\": \"some-token\"", json);
        Assert.Contains("\"clientToken\": \"some-client\"", json);
    }

    [Fact]
    public void RefreshRequest_ShouldSerializeCorrectly()
    {
        var request = new RefreshRequest
        {
            AccessToken = "old-token",
            ClientToken = "client-token",
            SelectedProfile = new ProfileInfo { Id = "uuid123", Name = "NewName" }
        };

        var json = JsonSerializer.Serialize(request, IndentedOptions);

        Assert.Contains("\"accessToken\": \"old-token\"", json);
        Assert.Contains("\"clientToken\": \"client-token\"", json);
        Assert.Contains("\"name\": \"NewName\"", json);
    }

    [Fact]
    public void YggErrorResponse_ShouldSerializeCorrectly()
    {
        var error = YggErrorResponse.InvalidCredentials();

        var json = JsonSerializer.Serialize(error, IndentedOptions);

        Assert.Contains("\"error\": \"ForbiddenOperationException\"", json);
        Assert.Contains("\"errorMessage\": \"Invalid credentials. Invalid username or password.\"", json);
    }

    [Fact]
    public void YggErrorResponse_FactoryMethods_ShouldProduceCorrectMessages()
    {
        var invalid = YggErrorResponse.InvalidCredentials();
        var token = YggErrorResponse.InvalidToken();
        var forbidden = YggErrorResponse.Forbidden("Custom message");

        Assert.Equal("ForbiddenOperationException", invalid.Error);
        Assert.Contains("Invalid username or password", invalid.ErrorMessage);

        Assert.Equal("ForbiddenOperationException", token.Error);
        Assert.Equal("Invalid token.", token.ErrorMessage);

        Assert.Equal("ForbiddenOperationException", forbidden.Error);
        Assert.Equal("Custom message", forbidden.ErrorMessage);
    }

    [Fact]
    public void ProfileResponse_ShouldSerializeWithSignature()
    {
        var profile = new ProfileResponse
        {
            Id = "abc123",
            Name = "Steve",
            Properties = new[]
            {
                new ProfileProperty
                {
                    Name = "textures",
                    Value = "base64encodedvalue",
                    Signature = "signature123"
                }
            }
        };

        var json = JsonSerializer.Serialize(profile, IndentedOptions);

        Assert.Contains("\"name\": \"textures\"", json);
        Assert.Contains("\"value\": \"base64encodedvalue\"", json);
        Assert.Contains("\"signature\": \"signature123\"", json);
    }

    [Fact]
    public void YggJsonContext_ShouldSerializeAllTypes()
    {
        var authReq = new AuthenticateRequest
        {
            Username = "test",
            Password = "pass"
        };
        var json1 = JsonSerializer.Serialize(authReq, YggJsonContext.Default.AuthenticateRequest);
        Assert.Contains("\"username\":\"test\"", json1);

        var error = YggErrorResponse.InvalidToken();
        var json2 = JsonSerializer.Serialize(error, YggJsonContext.Default.YggErrorResponse);
        Assert.Contains("\"error\":\"ForbiddenOperationException\"", json2);

        var validateReq = new ValidateRequest { AccessToken = "tok" };
        var json3 = JsonSerializer.Serialize(validateReq, YggJsonContext.Default.ValidateRequest);
        Assert.Contains("\"accessToken\":\"tok\"", json3);
    }
}
