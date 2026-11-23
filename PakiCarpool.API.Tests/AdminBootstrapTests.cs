using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PakiCarpool.Common.Constants;
using PakiCarpool.Domain.DTOs.Auth;
using PakiCarpool.Domain.Enums;
using PakiCarpool.Domain.Models;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Security.Helpers;
using PakiCarpool.Security.Middleware;
using PakiCarpool.Services.Implementations;
using Xunit;

namespace PakiCarpool.API.Tests;

public sealed class AdminBootstrapTests
{
    private const string SeedToken = "test-only-admin-bootstrap-seed-token";

    [Fact]
    public async Task ValidToken_CreatesFirstAdminWithVerifiedTotp()
    {
        var (service, repository) = CreateService();
        repository.Setup(repo => repo.CountByRoleAsync(UserRole.Admin)).ReturnsAsync(0);
        repository.Setup(repo => repo.EmailExistsAsync(It.IsAny<string>())).ReturnsAsync(false);
        User? createdUser = null;
        TotpSecret? createdTotp = null;
        repository.Setup(repo => repo.CreateFirstAdminAsync(
                It.IsAny<User>(),
                It.IsAny<TotpSecret>(),
                It.IsAny<CancellationToken>()))
            .Callback<User, TotpSecret, CancellationToken>((user, totp, _) =>
            {
                createdUser = user;
                createdTotp = totp;
            })
            .ReturnsAsync(true);

        var response = await service.BootstrapAsync(CreateRequest(), SeedToken);

        Assert.True(response.IsSuccess);
        Assert.NotNull(createdUser);
        Assert.Equal(UserRole.Admin, createdUser.Role);
        Assert.True(createdUser.IsEmailVerified);
        Assert.True(createdUser.IsActive);
        Assert.True(BCryptHelper.VerifyPassword("StrongPass1!", createdUser.PasswordHash!));
        Assert.NotNull(createdTotp);
        Assert.Equal(createdUser.Id, createdTotp.UserId);
        Assert.True(createdTotp.IsVerified);
        Assert.Equal(createdTotp.Secret, response.Data!.TotpSecret);
        Assert.Contains("otpauth://totp/", response.Data.OtpAuthUri, StringComparison.Ordinal);
        Assert.DoesNotContain(SeedToken, JsonSerializer.Serialize(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongToken_IsRejectedWithoutCreatingAdmin()
    {
        var (service, repository) = CreateService();

        var response = await service.BootstrapAsync(CreateRequest(), "wrong-seed-token");

        Assert.False(response.IsSuccess);
        Assert.Equal(ErrorCodes.Unauthorized, response.ErrorCode);
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MissingToken_IsRejectedWithoutCreatingAdmin()
    {
        var (service, repository) = CreateService();

        var response = await service.BootstrapAsync(CreateRequest(), null);

        Assert.False(response.IsSuccess);
        Assert.Equal(ErrorCodes.Unauthorized, response.ErrorCode);
        repository.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SecondAttempt_AfterAdminExists_IsRejected()
    {
        var (service, repository) = CreateService();
        repository.Setup(repo => repo.CountByRoleAsync(UserRole.Admin)).ReturnsAsync(1);

        var response = await service.BootstrapAsync(CreateRequest(), SeedToken);

        Assert.False(response.IsSuccess);
        Assert.Equal(ErrorCodes.NotFound, response.ErrorCode);
        repository.Verify(repo => repo.CreateFirstAdminAsync(
            It.IsAny<User>(),
            It.IsAny<TotpSecret>(),
            It.IsAny<CancellationToken>()), Times.Never);
        repository.Verify(repo => repo.EmailExistsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task BootstrapEndpoint_WhenAuthRateLimitIsHit_Returns429()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimit:AuthPerMinute"] = "2"
            })
            .Build();
        var nextCalls = 0;
        var middleware = new RateLimitingMiddleware(
            _ =>
            {
                nextCalls++;
                return Task.CompletedTask;
            },
            configuration,
            new InMemoryRateLimitCounter(),
            NullLogger<RateLimitingMiddleware>.Instance);

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/api/v1/auth/admin/bootstrap";
            context.Connection.RemoteIpAddress = IPAddress.Loopback;
            context.Response.Body = new MemoryStream();

            await middleware.InvokeAsync(context);

            if (attempt < 2)
                Assert.NotEqual(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
            else
                Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
        }

        Assert.Equal(2, nextCalls);
    }

    private static (AdminBootstrapService Service, Mock<IUserRepository> Repository) CreateService()
    {
        var repository = new Mock<IUserRepository>();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ADMIN_SEED_TOKEN"] = SeedToken
            })
            .Build();

        return (new AdminBootstrapService(repository.Object, configuration), repository);
    }

    private static AdminBootstrapRequestDto CreateRequest() => new()
    {
        FullName = "First Admin",
        Email = "first.admin@example.com",
        Password = "StrongPass1!"
    };
}
