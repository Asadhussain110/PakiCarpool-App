using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Moq;
using PakiCarpool.API.Helpers;
using PakiCarpool.API.Hubs;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Security.Helpers;
using Xunit;

namespace PakiCarpool.API.Tests;

public sealed class AccountAccessValidationTests
{
    [Fact]
    public async Task SuspendedUser_WithValidAccessToken_FailsApiTokenValidation()
    {
        var fixture = new AccountAccessFixture(isSuspended: true);

        await AccountAccessValidator.ValidateTokenAsync(fixture.TokenValidatedContext);

        Assert.NotNull(fixture.TokenValidatedContext.Result?.Failure);
        Assert.NotNull(fixture.Principal);
    }

    [Fact]
    public async Task SuspendedUser_WithValidAccessToken_IsAnonymousAfterBearerAuthentication()
    {
        var fixture = new AccountAccessFixture(isSuspended: true);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(fixture.Users.Object);
        services.AddSingleton(fixture.Revocations.Object);
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = fixture.Configuration["JWT_ISSUER"],
                ValidateAudience = true,
                ValidAudience = fixture.Configuration["JWT_AUDIENCE"],
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(
                    Encoding.UTF8.GetBytes(fixture.Configuration["JWT_SECRET"]!)),
                ValidateLifetime = true,
                ClockSkew = JwtHelper.ValidationClockSkew
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = AccountAccessValidator.ValidateTokenAsync
            };
        });
        await using var serviceProvider = services.BuildServiceProvider();
        var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
        httpContext.Request.Headers.Authorization = $"Bearer {fixture.AccessToken}";
        var downstreamCalled = false;
        var middleware = new AuthenticationMiddleware(
            _ =>
            {
                downstreamCalled = true;
                return Task.CompletedTask;
            },
            serviceProvider.GetRequiredService<IAuthenticationSchemeProvider>());

        await middleware.Invoke(httpContext);

        Assert.True(downstreamCalled);
        Assert.False(httpContext.User.Identity?.IsAuthenticated == true);
    }

    [Fact]
    public async Task SuspendedUser_WithValidAccessToken_CannotInvokeHubMethod()
    {
        var fixture = new AccountAccessFixture(isSuspended: true);
        var nextCalled = false;

        await Assert.ThrowsAsync<HubException>(() => fixture.HubFilter.InvokeMethodAsync(
            fixture.HubInvocationContext,
            _ =>
            {
                nextCalled = true;
                return ValueTask.FromResult<object?>(null);
            }).AsTask());

        Assert.False(nextCalled);
        fixture.Users.Verify(
            repository => repository.GetByIdAsync(fixture.User.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void ClockSkewBoundary_RejectsTokenImmediatelyAfterConfiguredExpiry()
    {
        var configuration = CreateConfiguration();
        var jwtHelper = new JwtHelper(configuration);
        var expiredToken = CreateToken(configuration, DateTime.UtcNow.AddSeconds(-1));
        var unexpiredToken = CreateToken(configuration, DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(TimeSpan.Zero, JwtHelper.ValidationClockSkew);
        Assert.Null(jwtHelper.ValidateToken(expiredToken));
        Assert.NotNull(jwtHelper.ValidateToken(unexpiredToken));
    }

    private static IConfiguration CreateConfiguration()
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JWT_SECRET"] = new string('v', 64),
                ["JWT_ISSUER"] = "validation-tests",
                ["JWT_AUDIENCE"] = "validation-test-clients",
                ["Auth:PassengerTokenExpiryMinutes"] = "30"
            })
            .Build();

    private static string CreateToken(IConfiguration configuration, DateTime expiresAt)
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configuration["JWT_SECRET"]!)),
            SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: configuration["JWT_ISSUER"],
            audience: configuration["JWT_AUDIENCE"],
            claims: [new Claim("userId", Guid.NewGuid().ToString())],
            expires: expiresAt,
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class AccountAccessFixture
    {
        public AccountAccessFixture(bool isSuspended)
        {
            Configuration = CreateConfiguration();
            var jwtHelper = new JwtHelper(Configuration);
            User = new User
            {
                Id = Guid.NewGuid(),
                Email = "suspended-validation@example.com",
                Role = UserRole.Passenger,
                IsActive = true,
                IsSuspended = isSuspended
            };
            AccessToken = jwtHelper.GenerateAccessToken(
                User.Id,
                User.Email,
                User.Role.ToString());
            Principal = jwtHelper.ValidateToken(AccessToken)
                ?? throw new InvalidOperationException("The unexpired access token must be valid.");

            Users.Setup(repository => repository.GetByIdAsync(User.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(User);
            Revocations.Setup(service => service.IsRevokedAsync(It.IsAny<string>()))
                .ReturnsAsync(false);
            var services = new ServiceCollection();
            services.AddSingleton(Users.Object);
            services.AddSingleton(Revocations.Object);
            var serviceProvider = services.BuildServiceProvider();
            var httpContext = new DefaultHttpContext { RequestServices = serviceProvider };
            TokenValidatedContext = new TokenValidatedContext(
                httpContext,
                new AuthenticationScheme(
                    JwtBearerDefaults.AuthenticationScheme,
                    displayName: null,
                    handlerType: typeof(JwtBearerHandler)),
                new JwtBearerOptions())
            {
                Principal = Principal
            };

            var callerContext = new Mock<HubCallerContext>();
            callerContext.SetupGet(context => context.User).Returns(Principal);
            callerContext.SetupGet(context => context.ConnectionAborted).Returns(CancellationToken.None);
            HubInvocationContext = new HubInvocationContext(
                callerContext.Object,
                serviceProvider,
                new TestHub(),
                typeof(TestHub).GetMethod(nameof(TestHub.Ping))!,
                Array.Empty<object>());
            HubFilter = new ActiveUserHubFilter(Users.Object);
        }

        public User User { get; }
        public IConfiguration Configuration { get; }
        public string AccessToken { get; }
        public ClaimsPrincipal Principal { get; }
        public TokenValidatedContext TokenValidatedContext { get; }
        public HubInvocationContext HubInvocationContext { get; }
        public ActiveUserHubFilter HubFilter { get; }
        public Mock<IUserRepository> Users { get; } = new();
        public Mock<ITokenRevocationService> Revocations { get; } = new();
    }

    private sealed class TestHub : Hub
    {
        public void Ping()
        {
        }
    }
}
