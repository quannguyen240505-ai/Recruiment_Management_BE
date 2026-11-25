using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Recruitment.Api.Data;
using Microsoft.AspNetCore.Identity;
using Recruitment.Api.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.OpenApi.Models;
using Recruitment.Api.Services.Auth;
using Recruitment.Api.Services.Jobs;


var builder = WebApplication.CreateBuilder(args);

// ---- Services (DI) ----
builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        // Enum trả về dạng OPEN, FULL_TIME... (khớp API Specification)
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper));
    });

builder.Services.AddEndpointsApiExplorer();

// 1. CẤU HÌNH SWAGGER ĐỂ NHẬP TOKEN (Thêm mới)
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Nhập JWT Token theo format: Bearer {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
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
            new string[] {}
        }
    });
});

builder.Services.AddHealthChecks();

// --- CẤU HÌNH DATABASE & BẢO MẬT (SCRUM-128) ---
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<JwtTokenService>();
builder.Services.AddScoped<IGoogleTokenValidator, GoogleTokenValidator>();

builder.Services.AddDataProtection();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddOptions<PasswordResetOptions>()
    .Bind(builder.Configuration.GetSection("PasswordReset"))
    .Validate(o => o.TokenLifetimeMinutes is >= 1 and <= 60, "Token lifetime must be 1–60 minutes.")
    .Validate(o => o.SmtpPort is >= 1 and <= 65535, "SMTP port is invalid.")
    .ValidateOnStart();
builder.Services.AddScoped<IPasswordResetEmailSender, SmtpPasswordResetEmailSender>();
builder.Services.AddScoped<IPasswordResetService, PasswordResetService>();
builder.Services.AddScoped<PasswordResetDeliveryService>();
builder.Services.AddHostedService<PasswordResetDeliveryWorker>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("password-reset", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
            }));
});

// --- CẤU HÌNH CORS (Cho phép Angular gọi API) ---
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngular", policy =>
    {
        policy.WithOrigins("http://localhost:4200") // Cổng mặc định của Angular
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

// --- CẤU HÌNH JWT AUTHENTICATION ---
var jwtKey = builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key is not configured.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

// 2. CẤU HÌNH PHÂN QUYỀN POLICIES (Thêm mới)
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("RequireCandidateRole", policy => policy.RequireRole("CANDIDATE"));
    options.AddPolicy("RequireRecruiterRole", policy => policy.RequireRole("RECRUITER"));
    options.AddPolicy("RequireAdminRole", policy => policy.RequireRole("ADMIN"));
});

// 3. CẤU HÌNH QUY ƯỚC LỖI CHUẨN (Thêm mới)
builder.Services.AddProblemDetails();

builder.Services.AddScoped<IJobService, JobService>();

var app = builder.Build();

// ---- Middleware pipeline ----
// 4. XỬ LÝ LỖI TOÀN CỤC (Thêm mới)
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseRouting();
app.UseCors("AllowAngular");

app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization(); // Đã xóa dòng bị lặp lại

app.MapControllers();
app.MapHealthChecks("/health");   // dùng cho kiểm tra hạ tầng / CI

app.Run();

// Entry point exposed for in-process HTTP integration tests.
public partial class Program { }
