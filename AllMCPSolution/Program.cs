using System.Text.Json.Serialization;
using AllMCPSolution.Models;
using AllMCPSolution.Services;
using AllMCPSolution.Services.Theming;
using AllMCPSolution.Hubs;
using AllMCPSolution.Utilities;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.AspNetCore.SignalR;

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
    .AddControllers(options =>
    {
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
        options.Filters.Add(new AuthorizeFilter(policy));
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services
    .AddControllersWithViews(options =>
    {
        var policy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();
        options.Filters.Add(new AuthorizeFilter(policy));
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });
builder.Services.AddRazorPages();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();

// SignalR for real-time notifications
builder.Services.AddSignalR();
builder.Services.AddSingleton<IUserIdProvider, NameIdentifierUserIdProvider>();

builder.Services.Configure<ChatGptOptions>(
    builder.Configuration.GetSection(ChatGptOptions.ConfigurationSectionName));

builder.Services.AddScoped<ICountryRepository, CountryRepository>();
builder.Services.AddScoped<IRegionRepository, RegionRepository>();
builder.Services.AddScoped<IAppellationRepository, AppellationRepository>();
builder.Services.AddScoped<ISubAppellationRepository, SubAppellationRepository>();
builder.Services.AddScoped<ISuggestedAppellationRepository, SuggestedAppellationRepository>();
builder.Services.AddScoped<IWineRepository, WineRepository>();
builder.Services.AddScoped<IWineVintageRepository, WineVintageRepository>();
builder.Services.AddScoped<IWineVintageEvolutionScoreRepository, WineVintageEvolutionScoreRepository>();
builder.Services.AddScoped<IWineVintageUserDrinkingWindowRepository, WineVintageUserDrinkingWindowRepository>();
builder.Services.AddScoped<IWishlistRepository, WishlistRepository>();
builder.Services.AddScoped<IWineVintageWishRepository, WineVintageWishRepository>();
builder.Services.AddScoped<IBottleRepository, BottleRepository>();
builder.Services.AddScoped<IBottleLocationRepository, BottleLocationRepository>();
builder.Services.AddScoped<IBottleShareRepository, BottleShareRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<ITasteProfileRepository, TasteProfileRepository>();
builder.Services.AddScoped<ITastingNoteRepository, TastingNoteRepository>();
builder.Services.AddScoped<ISisterhoodRepository, SisterhoodRepository>();
builder.Services.AddScoped<ISisterhoodInvitationRepository, SisterhoodInvitationRepository>();
builder.Services.AddScoped<ISipSessionRepository, SipSessionRepository>();
builder.Services.AddScoped<IWineSurferNotificationDismissalRepository, WineSurferNotificationDismissalRepository>();
builder.Services.AddScoped<ITerroirMergeRepository, TerroirMergeRepository>();
builder.Services.AddScoped<IWineSurferTopBarService, WineSurferTopBarService>();
builder.Services.AddScoped<ISisterhoodConnectionService, SisterhoodConnectionService>();
builder.Services.AddScoped<IBottleSummaryService, BottleSummaryService>();
builder.Services.AddScoped<IWineImportService, WineImportService>();
builder.Services.AddScoped<IStarWineListImportService, StarWineListImportService>();
builder.Services.AddScoped<ICellarTrackerImportService, CellarTrackerImportService>();
builder.Services.AddScoped<IChatGptService, ChatGptService>();
builder.Services.AddScoped<IChatGptPromptService, ChatGptPromptService>();
builder.Services.AddScoped<IWineCatalogService, WineCatalogService>();
builder.Services.AddScoped<IThemeService, ThemeService>();
builder.Services.AddScoped<ISuggestedAppellationService, SuggestedAppellationService>();
builder.Services.AddScoped<IUserDrinkingWindowService, UserDrinkingWindowService>();

builder.Services.AddScoped<IOcrService, AzureVisionOcrService>();

// Notifications
builder.Services.AddScoped<IUserNotificationService, UserNotificationService>();

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<Guid>>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireDigit = false;
        options.Password.RequiredLength = 1;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredUniqueChars = 1;
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



    app.UseDeveloperExceptionPage();


app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=WineSurfer}/{action=Index}/{id?}");
app.MapRazorPages();
app.MapControllers();

// Map SignalR hubs
app.MapHub<NotificationsHub>("/hubs/notifications");

app.MapGet("/", () => Results.Redirect("/wine-surfer"));

app.Run();
