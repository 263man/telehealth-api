using TelehealthApi.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// This line is what loads appsettings.json and then user secrets in development
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                     .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);

// This line specifically adds user secrets when in Development environment
builder.Configuration.AddUserSecrets<Program>(); // Or whatever your startup class is

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Register HttpClient with the correct BaseAddress
builder.Services.AddHttpClient("PatientOnboard", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["BaseAddress"] ?? "https://localhost:7259");
});

// Configure HTTPS redirection to use the correct port
builder.Services.AddHttpsRedirection(options =>
{
    options.RedirectStatusCode = StatusCodes.Status307TemporaryRedirect;
    options.HttpsPort = 7259;
});

// Optional: Configure HSTS for production
builder.Services.AddHsts(options =>
{
    options.Preload = true;
    options.IncludeSubDomains = true;
    options.MaxAge = TimeSpan.FromDays(60);
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStaticFiles();

app.UseHttpsRedirection();
app.UseWebSockets();
// Only include antiforgery if necessary (see below)
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapGet("/test-static", () => Results.File("wwwroot/app.css", "text/css"));
app.Run();