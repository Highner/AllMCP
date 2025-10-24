using System.Text.Json.Serialization;
using AllMCPSolution.Models;
using AllMCPSolution.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure();
        }));

builder.Services
    .AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services
    .AddControllersWithViews()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddRazorPages();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();

builder.Services.AddScoped<ICountryRepository, CountryRepository>();
builder.Services.AddScoped<IRegionRepository, RegionRepository>();
builder.Services.AddScoped<IAppellationRepository, AppellationRepository>();
builder.Services.AddScoped<ISubAppellationRepository, SubAppellationRepository>();
builder.Services.AddScoped<ISuggestedAppellationRepository, SuggestedAppellationRepository>();
builder.Services.AddScoped<IWineRepository, WineRepository>();
builder.Services.AddScoped<IWineVintageRepository, WineVintageRepository>();
builder.Services.AddScoped<IWineVintageEvolutionScoreRepository, WineVintageEvolutionScoreRepository>();
builder.Services.AddScoped<IBottleRepository, BottleRepository>();
builder.Services.AddScoped<IBottleLocationRepository, BottleLocationRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ITastingNoteRepository, TastingNoteRepository>();
builder.Services.AddScoped<ISisterhoodRepository, SisterhoodRepository>();
builder.Services.AddScoped<ISisterhoodInvitationRepository, SisterhoodInvitationRepository>();
builder.Services.AddScoped<ISipSessionRepository, SipSessionRepository>();
builder.Services.AddScoped<IWineSurferNotificationDismissalRepository, WineSurferNotificationDismissalRepository>();
builder.Services.AddScoped<ITerroirMergeRepository, TerroirMergeRepository>();
builder.Services.AddScoped<IWineSurferTopBarService, WineSurferTopBarService>();
builder.Services.AddScoped<IWineImportService, WineImportService>();
builder.Services.AddScoped<IChatGptService, ChatGptService>();
builder.Services.AddScoped<IChatGptPromptService, ChatGptPromptService>();
builder.Services.AddScoped<IWineCatalogService, WineCatalogService>();

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    // Match attribute-routed endpoints in AccountController [Route("account")]
    options.LoginPath = "/account/login";
    options.LogoutPath = "/account/logout";
});

builder.Services.AddAuthorization();

var apiKey = builder.Configuration["OpenAI:ApiKey"];
if (string.IsNullOrEmpty(apiKey))
{
    builder.Logging.AddConsole();
    Console.WriteLine("⚠️ OpenAI API key not found in configuration");
}
else
{
    Console.WriteLine("✅ OpenAI API key detected");
}

var app = builder.Build();

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
});
app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=WineSurfer}/{action=Index}/{id?}");
app.MapRazorPages();
app.MapControllers();

app.MapGet("/", () => Results.Redirect("/wine-surfer"));

app.Run();
