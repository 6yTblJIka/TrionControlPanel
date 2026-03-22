using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Trion.API.Data;
using Trion.API.Endpoints;
using Trion.API.Services;

var builder = WebApplication.CreateBuilder(args);

// ── JSON: camelCase + ignore nulls ────────────────────────────────────────────
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy        = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.PropertyNameCaseInsensitive = true;
});

// ── JWT ───────────────────────────────────────────────────────────────────────
var jwtCfg = builder.Configuration.GetSection("Jwt");
var jwtKey  = jwtCfg["SecretKey"]
    ?? throw new InvalidOperationException("Jwt:SecretKey is not configured.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateIssuer           = true,
            ValidIssuer              = jwtCfg["Issuer"],
            ValidateAudience         = true,
            ValidAudience            = jwtCfg["Audience"],
            ClockSkew                = TimeSpan.Zero,
        };
    });

builder.Services.AddAuthorization(o =>
    o.AddPolicy("AdminPolicy", p => p.RequireRole("admin")));

// ── Rate limiting ─────────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(o =>
{
    o.AddFixedWindowLimiter("login", opt =>
    {
        opt.Window      = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 10;
        opt.QueueLimit  = 0;
    });
    o.AddConcurrencyLimiter("download", opt =>
    {
        opt.PermitLimit = 5;
        opt.QueueLimit  = 2;
    });
    o.AddFixedWindowLimiter("downloads-ip", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window      = TimeSpan.FromMinutes(1);
        opt.QueueLimit  = 0;
    });
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// ── CORS ──────────────────────────────────────────────────────────────────────
builder.Services.AddCors(o =>
    o.AddDefaultPolicy(p => p
        .WithOrigins("https://flying-phoenix.dev", "https://www.flying-phoenix.dev")
        .AllowAnyMethod()
        .AllowAnyHeader()));

// ── Caching ───────────────────────────────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.AddOutputCache();

// ── Allow large multipart uploads (admin upload-emulator endpoint) ─────────────
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 10L * 1024 * 1024 * 1024; // 10 GB — enough for any emulator package
    o.ValueLengthLimit         = 128 * 1024;                // 128 KB for form fields
});

// ── OpenAPI / Swagger ─────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Trion API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new()
    {
        Name         = "Authorization",
        Type         = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme       = "bearer",
        BearerFormat = "JWT",
        In           = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description  = "Enter JWT token",
    });
    c.AddSecurityRequirement(new()
    {
        {
            new() { Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } },
            []
        }
    });
});

// ── App services ──────────────────────────────────────────────────────────────
builder.Services.AddSingleton<TrionDbAccess>();
builder.Services.AddSingleton<IJwtService, JwtService>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<INewsService, NewsService>();

// ── Startup validation: reject placeholder secrets ────────────────────────────
var adminKey  = builder.Configuration["AdminApiKey"] ?? "";
var secretKey = builder.Configuration["Jwt:SecretKey"] ?? "";
if (adminKey.StartsWith("change-this") || secretKey.StartsWith("change-this"))
    throw new InvalidOperationException(
        "Default placeholder secrets detected. Set real values via environment variables " +
        "(TRION_AdminApiKey, TRION_Jwt__SecretKey) before running in production.");

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Forward headers (X-Forwarded-For / Proto behind reverse proxy) ────────────
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});

app.UseSwagger();
app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "Trion API v1"));

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseCors();
app.UseRateLimiter();
app.UseOutputCache();
app.UseAuthentication();
app.UseAuthorization();

// ── Minimal API endpoint groups ───────────────────────────────────────────────
app.MapPackageEndpoints();
app.MapAccountEndpoints();
app.MapAdminEndpoints();
app.MapNewsEndpoints();
app.MapInfoEndpoints();

app.Run();
