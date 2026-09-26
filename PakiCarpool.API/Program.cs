using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PakiCarpool.API.Helpers;
using PakiCarpool.API.Hubs;
using PakiCarpool.API.Middleware;
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
using System.Text;

var builder = WebApplication.CreateBuilder(args);

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
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

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

// ─────────────────────────────────────────────────────────────
//  4. SECURITY HELPERS
// ─────────────────────────────────────────────────────────────
builder.Services.AddSingleton<JwtHelper>();
builder.Services.AddSingleton<AesEncryptionHelper>();

// ─────────────────────────────────────────────────────────────
//  5. APPLICATION SERVICES (11)
// ─────────────────────────────────────────────────────────────
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IDriverService, DriverService>();
builder.Services.AddScoped<IPassengerService, PassengerService>();
builder.Services.AddScoped<IChatService, ChatService>();
builder.Services.AddScoped<IAdminService, AdminService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ILivenessService, LivenessService>();
builder.Services.AddScoped<ITrustService, TrustService>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IFlagService, FlagService>();
builder.Services.AddScoped<IRatingService, RatingService>();
builder.Services.AddHttpClient<IHCaptchaService, HCaptchaService>();

// ─────────────────────────────────────────────────────────────
//  6. BACKGROUND JOBS (5)
// ─────────────────────────────────────────────────────────────
builder.Services.AddHostedService<BookingTimeoutJob>();
builder.Services.AddHostedService<ChatExpiryJob>();
builder.Services.AddHostedService<RefreshTokenCleanupJob>();
builder.Services.AddHostedService<TrustScoreJob>();
builder.Services.AddHostedService<RideGraceExpiryJob>();

// ─────────────────────────────────────────────────────────────
//  7. SIGNALR
// ─────────────────────────────────────────────────────────────
builder.Services.AddSignalR();
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
    ?? builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("JWT secret is not configured. Set JWT_SECRET env var or Jwt:Secret in appsettings.");

var jwtIssuer = builder.Configuration["JWT_ISSUER"]
    ?? builder.Configuration["Jwt:Issuer"]
    ?? "api.pakicarpool.pk";

var jwtAudience = builder.Configuration["JWT_AUDIENCE"]
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
        ClockSkew = TimeSpan.FromMinutes(5)
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
        }
    };
});

// ─────────────────────────────────────────────────────────────
//  11. AUTHORIZATION — Role-based policies
// ─────────────────────────────────────────────────────────────
builder.Services.AddAuthorization(RolePolicy.Configure);

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

app.UseHttpsRedirection();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseCors("PakiCarpoolPolicy");
app.UseMiddleware<RateLimitingMiddleware>();
app.UseMiddleware<JwtMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<PakiCarpoolHub>("/hubs/pakicarpool");

var aesEncryptionHelper = app.Services.GetRequiredService<AesEncryptionHelper>();
var passwordHash = BCryptHelper.HashPassword("AdminPassword123!");
var totpSecret = TotpHelper.GenerateSecret();
var encryptedTotpSecret = aesEncryptionHelper.Encrypt(totpSecret);
Console.Error.WriteLine($"PasswordHash: {passwordHash}");
Console.Error.WriteLine($"Plain TOTP Secret: {totpSecret}");
Console.Error.WriteLine($"Encrypted TOTP Secret: {encryptedTotpSecret}");

app.Run();
