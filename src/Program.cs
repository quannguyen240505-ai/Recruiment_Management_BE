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
var jwtSecret = builder.Configuration["Jwt:Secret"] ?? throw new InvalidOperationException("JWT Secret is not configured.");
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
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
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

// TODO: đăng ký các service nghiệp vụ tại đây, ví dụ:
// builder.Services.AddScoped<IJobService, JobService>();

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

app.UseCors("AllowAngular");

app.UseAuthentication();
app.UseAuthorization(); // Đã xóa dòng bị lặp lại

app.MapControllers();
app.MapHealthChecks("/health");   // dùng cho kiểm tra hạ tầng / CI

app.Run();
