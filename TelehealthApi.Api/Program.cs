using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using TelehealthApi.Core;
using TelehealthApi.Core.Interfaces;
using TelehealthApi.Core.Services;

// All top-level statements must come first in Program.cs
var builder = WebApplication.CreateBuilder(args);

// Configure Serilog for application logging
builder.Host.UseSerilog((ctx, lc) => lc
    .MinimumLevel.Debug()
    .ReadFrom.Configuration(ctx.Configuration) // Reads from appsettings.json
    .WriteTo.Console()
    .WriteTo.File("logs/application.log", rollingInterval: RollingInterval.Day,
                  outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"));

// Adding services to the container.
// Ensure these DbContext registrations also get the connection string correctly from the main application's configuration
builder.Services.AddDbContextFactory<TelehealthDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddDbContext<TelehealthDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var url = config["FhirServer:Url"]
              ?? throw new InvalidOperationException("Missing FhirServer:Url configuration.");
    return new FhirClient(url);
});
builder.Services.AddSingleton<IFhirService, FhirService>();
builder.Services.AddSingleton<EncryptionService>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();
builder.Services.AddScoped<IPatientService, PatientService>();
builder.Services.AddIdentity<IdentityUser, IdentityRole>()
    .AddEntityFrameworkStores<TelehealthDbContext>()
    .AddDefaultTokenProviders();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.SaveToken = true;
    options.RequireHttpsMetadata = builder.Environment.IsProduction();
    options.TokenValidationParameters = new TokenValidationParameters()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key is not configured.")))
    };
});
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(7164, listenOptions =>
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        var cert = store.Certificates.Find(X509FindType.FindBySubjectName, "localhost", false)[0];
        store.Close();
        listenOptions.UseHttps(cert);
    });
    options.ListenLocalhost(5141); // Keep HTTP for dev fallback
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Design-time DbContext factory for EF Core migrations
// This class must be declared AFTER all top-level statements in Program.cs
public class TelehealthDbContextFactory : IDesignTimeDbContextFactory<TelehealthDbContext>
{
    public TelehealthDbContext CreateDbContext(string[] args)
    {
        // Build configuration for design-time operations
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            // Add environment-specific appsettings file, e.g., appsettings.Development.json
            // The ASPNETCORE_ENVIRONMENT variable might not always be set when running dotnet ef commands
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"}.json", optional: true, reloadOnChange: true)
            // Add user secrets, which should contain your DefaultConnection string
            .AddUserSecrets<Program>()
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<TelehealthDbContext>();

        // Explicitly get the connection string and check for null
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        if (string.IsNullOrEmpty(connectionString))
        {
            // If the connection string is still null or empty, throw a more specific error.
            // This indicates that either User Secrets are not being loaded, or the key is wrong.
            throw new InvalidOperationException(
                "The 'DefaultConnection' connection string was not found in appsettings.json, " +
                "appsettings.Development.json, or User Secrets. " +
                "Please ensure it is configured correctly." +
                $" Current ASPNETCORE_ENVIRONMENT: {Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Not Set (Defaulting to Development)"}"
            );
        }

        optionsBuilder.UseSqlServer(connectionString);

        return new TelehealthDbContext(optionsBuilder.Options);
    }
}
