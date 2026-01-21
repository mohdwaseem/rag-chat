// Program.cs
using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using RAGDemoBackend.Data;
using RAGDemoBackend.Models;
using RAGDemoBackend.Services;
using RAGDemoBackend.Services.HealthChecks;
using Serilog;
using Serilog.Context;
using Qdrant.Client;
using System.IdentityModel.Tokens.Jwt;

// Configure Serilog from appsettings.json
var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json")
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
    .Build();

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .CreateLogger();

try
{
    // Ensure Arabic and other Unicode text renders correctly in logs on Windows.
    try
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.InputEncoding = System.Text.Encoding.UTF8;
    }
    catch
    {
        // Ignore if host does not allow changing encoding.
    }

    Log.Information("Starting RAGDemoBackend application");

    var builder = WebApplication.CreateBuilder(args);

    // Add Serilog
    builder.Host.UseSerilog();

// Add services
builder.Services
    .AddControllers()
    .AddJsonOptions(o =>
    {
        // Ensure consistent UTF-8 handling for Arabic and other non-Latin languages.
        o.JsonSerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var problemDetails = new ValidationProblemDetails(context.ModelState)
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Request validation failed"
            };
            return new BadRequestObjectResult(problemDetails);
        };
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddProblemDetails();

builder.Services.AddExceptionHandler<ApiExceptionHandler>();

// Dependency health checks
builder.Services.AddHealthChecks()
    .AddCheck<QdrantHealthCheck>("qdrant", tags: ["ready"]) 
    .AddCheck<EmbeddingModelHealthCheck>("embedding_model", tags: ["ready"]);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // Default global limiter (applies to all endpoints unless overridden)
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue<int>("RateLimiting:Global:PermitLimit", 300),
            Window = TimeSpan.FromMinutes(builder.Configuration.GetValue<int>("RateLimiting:Global:WindowMinutes", 1)),
            QueueLimit = builder.Configuration.GetValue<int>("RateLimiting:Global:QueueLimit", 0),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
    });

    // Stricter limiter for model endpoints
    options.AddPolicy("chat", context =>
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue<int>("RateLimiting:Chat:PermitLimit", 30),
            Window = TimeSpan.FromMinutes(builder.Configuration.GetValue<int>("RateLimiting:Chat:WindowMinutes", 1)),
            QueueLimit = builder.Configuration.GetValue<int>("RateLimiting:Chat:QueueLimit", 0),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });
    });
});

// JWT Authentication
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "RAGDemoBackend";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "RAGDemoFrontend";
var jwtKey = builder.Configuration["Jwt:Key"] ?? "CHANGE_ME_IN_PRODUCTION";

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            RoleClaimType = "role",
            NameClaimType = JwtRegisteredClaimNames.Sub // Set User.Identity.Name to sub claim
        };
    });

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("Admin", policy =>
        {
            policy.RequireAuthenticatedUser();
            policy.RequireRole("admin", "ADMIN", "Admin"); // All cases
        });
    });

    builder.Services.AddHttpLogging(o =>
{
    o.LoggingFields = HttpLoggingFields.RequestMethod |
                      HttpLoggingFields.RequestPath |
                      HttpLoggingFields.ResponseStatusCode |
                      HttpLoggingFields.Duration;
});

builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<GzipCompressionProviderOptions>(o =>
{
    o.Level = CompressionLevel.Fastest;
});

// Add RAG services
builder.Services.AddSingleton<IEmbeddingService, LocalEmbeddingService>();
builder.Services.AddSingleton<IVectorStoreService, QdrantVectorStoreService>();
builder.Services.AddScoped<IDocumentService, DocumentService>();
builder.Services.AddScoped<IChatService, ChatService>();

var connectionString = builder.Configuration.GetConnectionString("RagDemoDb")
                      ?? "Data Source=localhost;Initial Catalog=RagDemoDB;Persist Security Info=True;User ID=sa;Password=Aa@123456;Pooling=False;MultipleActiveResultSets=False;Encrypt=False;TrustServerCertificate=True;Application Name=\"SQL Server Management Studio\";Command Timeout=0";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services
    .AddIdentityCore<ApplicationUser>(options =>
    {
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = false;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager();

// Register a shared QdrantClient for health checks (and optionally other services)
builder.Services.AddSingleton(sp =>
{
    var cfg = sp.GetRequiredService<IConfiguration>();
    var host = cfg["Qdrant:Host"] ?? "localhost";
    var port = int.Parse(cfg["Qdrant:Port"] ?? "6334");
    var useHttps = bool.Parse(cfg["Qdrant:UseHttps"] ?? "false");
    return new QdrantClient(host, port, useHttps);
});

// Background init for vector store
builder.Services.AddHostedService<QdrantInitializerHostedService>();

// Configure CORS for frontend(s)
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
                     ?? new[] { "http://localhost:4200" };
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        var origins = allowedOrigins;

        if (!builder.Environment.IsDevelopment())
        {
            origins = origins.Where(o =>
            {
                if (!Uri.TryCreate(o, UriKind.Absolute, out var uri))
                {
                    return false;
                }

                return uri.Scheme == Uri.UriSchemeHttps;
            }).ToArray();
        }

        policy.WithOrigins(origins)
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var dbContext = services.GetRequiredService<ApplicationDbContext>();
    await dbContext.Database.EnsureCreatedAsync();

    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

    if (!await roleManager.RoleExistsAsync("admin"))
    {
        await roleManager.CreateAsync(new IdentityRole("admin"));
    }

    if (!await roleManager.RoleExistsAsync("user"))
    {
        await roleManager.CreateAsync(new IdentityRole("user"));
    }

    var adminUsername = builder.Configuration["Auth:AdminUsername"] ?? "admin";
    var adminPassword = builder.Configuration["Auth:AdminPassword"] ?? "Admin@123456";
    var adminUser = await userManager.FindByNameAsync(adminUsername);

    if (adminUser == null)
    {
        adminUser = new ApplicationUser
        {
            UserName = adminUsername,
            Email = $"{adminUsername}@local"
        };

        var createResult = await userManager.CreateAsync(adminUser, adminPassword);
        if (createResult.Succeeded)
        {
            await userManager.AddToRoleAsync(adminUser, "admin");
        }
    }
    else
    {
        var roles = await userManager.GetRolesAsync(adminUser);
        if (!roles.Contains("admin"))
        {
            await userManager.AddToRoleAsync(adminUser, "admin");
        }
    }
}

 if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Required when running behind reverse proxies / load balancers
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});

app.UseExceptionHandler();
app.UseStatusCodePages();

app.Use(async (context, next) =>
{
    const string headerName = "X-Request-Id";
    if (!context.Request.Headers.TryGetValue(headerName, out var requestId) || string.IsNullOrWhiteSpace(requestId))
    {
        requestId = Guid.NewGuid().ToString("n");
        context.Request.Headers[headerName] = requestId;
    }
    context.Response.Headers[headerName] = requestId;

    var userId = context.User?.Identity?.IsAuthenticated == true
        ? context.User.Identity?.Name ?? context.User.FindFirst("sub")?.Value
        : null;

    using (LogContext.PushProperty("RequestId", requestId.ToString()))
    using (LogContext.PushProperty("TraceId", context.TraceIdentifier))
    using (LogContext.PushProperty("Path", context.Request.Path.ToString()))
    using (LogContext.PushProperty("UserId", userId))
    {
        await next();
    }
});

 app.UseHttpsRedirection();
 app.UseResponseCompression();
 app.UseHttpLogging();
 app.UseRateLimiter();
 app.UseCors("AllowFrontend");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResultStatusCodes =
    {
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy] = StatusCodes.Status200OK,
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
        [Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable
    },
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description
            })
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
});

// Create necessary folders
var sampleDocsPath = Path.Combine(Directory.GetCurrentDirectory(), "Data", "SampleDocuments");
Directory.CreateDirectory(sampleDocsPath);

var modelsPath = Path.Combine(Directory.GetCurrentDirectory(), "Models");
Directory.CreateDirectory(modelsPath);

// Initialize Qdrant collection

app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}