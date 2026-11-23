using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using StackExchange.Redis;
using PakiCarpool.API.Configuration;
using PakiCarpool.API.Health;
using PakiCarpool.API.Helpers;
using PakiCarpool.API.Hubs;
using PakiCarpool.API.Middleware;
using PakiCarpool.Common;
using PakiCarpool.Interfaces.IRepositories;
using PakiCarpool.Interfaces.IServices;
using PakiCarpool.Repository.Dapper;
using PakiCarpool.Repository.EFCore;
using PakiCarpool.Repository.EFCore.Repositories;
using PakiCarpool.Security.Helpers;
using PakiCarpool.Security.Middleware;
using PakiCarpool.Security.Policies;
using PakiCarpool.Services.BackgroundJobs;
using PakiCarpool.Services.Implementations;
using PakiCarpool.Services.Options;
using System.Text;

LoadDotEnv();

var builder = WebApplication.CreateBuilder(args);
RequiredConfigurationValidator.Validate(builder.Configuration, requireRedis: !builder.Environment.IsDevelopment());

// ─────────────────────────────────────────────────────────────
//  1. CONTROLLERS + JSON
// ─────────────────────────────────────────────────────────────
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        options.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    });

// ─────────────────────────────────────────────────────────────
//  2. DATABASE — EF Core + PostgreSQL + Dapper factory
// ─────────────────────────────────────────────────────────────
var connectionString = Environment.GetEnvironmentVariable("DB_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
    throw new InvalidOperationException("DB_CONNECTION_STRING is not configured. Set the single Supabase Postgres connection string before starting the backend.");

builder.Services.AddDbContext<PakiCarpoolDbContext>((serviceProvider, options) =>
{
    options.UseNpgsql(connectionString, npgsqlOptions =>
    {
        npgsqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorCodesToAdd: null);
    });

    if (builder.Environment.IsDevelopment())
    {
        options.EnableDetailedErrors();
        options.EnableSensitiveDataLogging(false);
    }
});

builder.Services.AddSingleton<IDapperConnectionFactory>(_ => new NpgsqlConnectionFactory(connectionString));
builder.Services.AddScoped<SeatCapacityQuery>();
builder.Services.AddScoped<RideSearchQuery>();
builder.Services.AddScoped<RideAnalyticsQuery>();
builder.Services.AddScoped<LiveLocationQuery>();
builder.Services.AddScoped<ReportQuery>();

// ─────────────────────────────────────────────────────────────
//  3. REPOSITORIES
// ─────────────────────────────────────────────────────────────
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IRideRepository, RideRepository>();
builder.Services.AddScoped<IBookingRepository, BookingRepository>();
builder.Services.AddScoped<IChatRepository, ChatRepository>();
builder.Services.AddScoped<IAdminRepository, AdminRepository>();
builder.Services.AddScoped<ILivenessChallengeRepository, LivenessChallengeRepository>();

// ─────────────────────────────────────────────────────────────
//  4. SECURITY HELPERS
// ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<JwtHelper>();
builder.Services.AddSingleton<AesEncryptionHelper>();
builder.Services.AddSingleton<ITokenRevocationService, TokenRevocationService>();

// ─────────────────────────────────────────────────────────────
//  5. APPLICATION SERVICES
// ─────────────────────────────────────────────────────────────
builder.Services.AddScoped<IUserSessionService, UserSessionService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IAdminBootstrapService, AdminBootstrapService>();
builder.Services.AddScoped<IDriverService, DriverService>();
builder.Services.AddScoped<IPassengerService, PassengerService>();
builder.Services.AddScoped<IRideSearchService, RideSearchService>();
builder.Services.AddHttpClient<IPlacesService, PlacesService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ILivenessService, LivenessService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<Supabase.Client>(_ => new Supabase.Client(
    builder.Configuration["SUPABASE_URL"] ?? Environment.GetEnvironmentVariable("SUPABASE_URL") ?? string.Empty,
    builder.Configuration["SUPABASE_KEY"] ?? Environment.GetEnvironmentVariable("SUPABASE_KEY") ?? string.Empty,
    new Supabase.SupabaseOptions { AutoConnectRealtime = false }));
builder.Services.AddSingleton<IStorageService, SupabaseStorageService>();
builder.Services.AddScoped<ITrustService, TrustService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IFlagService, FlagService>();
builder.Services.AddScoped<IRatingService, RatingService>();
builder.Services.AddHttpClient<IHCaptchaService, HCaptchaService>();
builder.Services.AddHttpClient("GoogleOAuth", client =>
{
    client.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.Configure<RideSearchOptions>(
    builder.Configuration.GetSection(RideSearchOptions.SectionName));
builder.Services.Configure<PhotonOptions>(
    builder.Configuration.GetSection(PhotonOptions.SectionName));

// ─────────────────────────────────────────────────────────────
//  6. BACKGROUND JOBS (7)
// ─────────────────────────────────────────────────────────────
builder.Services.AddHostedService<HubThrottleCleanupService>();
builder.Services.AddHostedService<BookingTimeoutJob>();
builder.Services.AddHostedService<ChatExpiryJob>();
builder.Services.AddHostedService<RefreshTokenCleanupJob>();
builder.Services.AddHostedService<TrustScoreJob>();
builder.Services.AddHostedService<RideGraceExpiryJob>();
builder.Services.AddHostedService<LivenessChallengeCleanupJob>();

// ─────────────────────────────────────────────────────────────
//  7. SIGNALR
// ─────────────────────────────────────────────────────────────
var redisConnectionString = builder.Configuration["REDIS_CONNECTION_STRING"];
if (string.IsNullOrWhiteSpace(redisConnectionString))
    redisConnectionString = builder.Configuration["Redis:ConnectionString"];

IConnectionMultiplexer? developmentRedisConnection = null;
if (builder.Environment.IsDevelopment())
{
    if (string.IsNullOrWhiteSpace(redisConnectionString))
    {
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
        loggerFactory.CreateLogger("PakiCarpool.API")
            .LogWarning("Redis is unset; using in-memory SignalR and local development state.");
    }
    else
    {
        try
        {
            var redisOptions = ConfigurationOptions.Parse(redisConnectionString);
            redisOptions.AbortOnConnectFail = true;
            redisOptions.ConnectTimeout = 2000;
            redisOptions.ConnectRetry = 0;
            developmentRedisConnection = ConnectionMultiplexer.Connect(redisOptions);
        }
        catch (Exception exception) when (
            exception is RedisConnectionException
                or TimeoutException
                or System.Net.Sockets.SocketException
                or ArgumentException
                or FormatException)
        {
            redisConnectionString = null;
            using var loggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
            loggerFactory.CreateLogger("PakiCarpool.API")
                .LogWarning("Redis is unreachable; using in-memory SignalR and local development state.");
        }
    }
}

if (!string.IsNullOrWhiteSpace(redisConnectionString))
{
    if (developmentRedisConnection is not null)
        builder.Services.AddSingleton<IConnectionMultiplexer>(developmentRedisConnection);
    else
        builder.Services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisConnectionString));
    builder.Services.AddSingleton<IRateLimitCounter, RedisRateLimitCounter>();
    builder.Services.AddSingleton<IRideGroupConnectionTracker, RedisRideGroupConnectionTracker>();
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
    });
    builder.Services.AddSignalR(options => options.AddFilter<ActiveUserHubFilter>())
        .AddStackExchangeRedis(redisConnectionString, options =>
        {
            options.Configuration.ChannelPrefix = RedisChannel.Literal("pakicarpool");
        });
    builder.Services.AddSingleton<IOAuthStateStore, RedisOAuthStateStore>();
}
else
{
    builder.Services.AddDistributedMemoryCache();
    builder.Services.AddSingleton<IRateLimitCounter, InMemoryRateLimitCounter>();
    builder.Services.AddSingleton<IRideGroupConnectionTracker, InMemoryRideGroupConnectionTracker>();
    builder.Services.AddSignalR(options => options.AddFilter<ActiveUserHubFilter>());
    builder.Services.AddSingleton<IOAuthStateStore, OAuthStateStore>();
}
builder.Services.AddSingleton<IUserIdProvider, NameUserIdProvider>();
builder.Services.AddSingleton<IHubContext<Hub>>(sp =>
    new HubContextBridge(sp.GetRequiredService<IHubContext<PakiCarpoolHub>>()));

// ─────────────────────────────────────────────────────────────
//  8. CORS
// ─────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["https://app.pakicarpool.pk", "https://admin.pakicarpool.pk"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("PakiCarpoolPolicy", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials();
    });
});

// ─────────────────────────────────────────────────────────────
//  9. HSTS
// ─────────────────────────────────────────────────────────────
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
    options.Preload = true;
});

// ─────────────────────────────────────────────────────────────
//  10. AUTHENTICATION — JWT Bearer (+ SignalR access_token)
// ─────────────────────────────────────────────────────────────
var jwtSecret = builder.Configuration["JWT_SECRET"]
    ?? Environment.GetEnvironmentVariable("JWT_SECRET")
    ?? builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("JWT secret is not configured. Set JWT_SECRET env var or Jwt:Secret in appsettings.");

var jwtIssuer = builder.Configuration["JWT_ISSUER"]
    ?? Environment.GetEnvironmentVariable("JWT_ISSUER")
    ?? builder.Configuration["Jwt:Issuer"]
    ?? "api.pakicarpool.pk";

var jwtAudience = builder.Configuration["JWT_AUDIENCE"]
    ?? Environment.GetEnvironmentVariable("JWT_AUDIENCE")
    ?? builder.Configuration["Jwt:Audience"]
    ?? "pakicarpool-clients";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ValidateLifetime = true,
        ClockSkew = JwtHelper.ValidationClockSkew
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
            {
                context.Token = accessToken;
            }

            return Task.CompletedTask;
        },
        OnTokenValidated = AccountAccessValidator.ValidateTokenAsync
    };
});

// ─────────────────────────────────────────────────────────────
//  11. AUTHORIZATION — Role-based policies
// ─────────────────────────────────────────────────────────────
builder.Services.AddAuthorization(RolePolicy.Configure);

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database");

// ─────────────────────────────────────────────────────────────
//  12. SWAGGER / OPENAPI
// ─────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "PakiCarpool API",
        Version = "v1",
        Description = "REST API + SignalR for the PakiCarpool carpooling platform.",
        Contact = new OpenApiContact { Name = "PakiCarpool Team" }
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Enter your JWT token. Example: Bearer {token}"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// ─────────────────────────────────────────────────────────────
//  APPLICATION PIPELINE (ORDER MATTERS)
// ─────────────────────────────────────────────────────────────
var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseMiddleware<ExceptionHandlingMiddleware>();

var enableSwagger = app.Configuration.GetValue("Swagger:Enable", app.Environment.IsDevelopment());
if (enableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "PakiCarpool API v1");
        options.RoutePrefix = "swagger";
    });
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
    app.UseHsts();
}

app.UseCors("PakiCarpoolPolicy");
app.UseMiddleware<RateLimitingMiddleware>();
app.UseAuthentication();
app.UseMiddleware<ClientIdRateLimitMiddleware>();
app.UseMiddleware<LocationRateLimitMiddleware>();
app.UseAuthorization();

app.MapControllers();
app.MapHub<PakiCarpoolHub>("/hubs/pakicarpool");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Name == "database"
});

app.Run();

static void LoadDotEnv()
{
    var startDirectories = new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };
    foreach (var startDirectory in startDirectories)
    {
        for (var directory = new DirectoryInfo(startDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var envPath = Path.Combine(directory.FullName, ".env");
            if (!File.Exists(envPath))
                continue;

            DotNetEnv.Env.NoClobber().Load(envPath);
            return;
        }
    }
}
