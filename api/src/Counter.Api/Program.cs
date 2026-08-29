using Counter.Api.Filters;
using Counter.Api.Services;
using Counter.Infrastructure.Catalogue;
using Counter.Infrastructure.Data;
using Counter.Infrastructure.Erp;
using Counter.Infrastructure.Mcp;
using Microsoft.EntityFrameworkCore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ErpConnectionOptions>(
    builder.Configuration.GetSection(ErpConnectionOptions.SectionName));

// One reader for the process. It holds a single MCP connection, which is the
// same reasoning the MCP server applies to its own database session: a
// connection per request leaks one per burst.
builder.Services.AddSingleton<IErpReader, ErpReader>();
builder.Services.AddSingleton<BranchDirectory>();
builder.Services.AddSingleton<CatalogueProjection>();
builder.Services.AddSingleton<CustomerDirectory>();
builder.Services.AddSingleton<SessionStore>();
builder.Services.AddSingleton<ActivityRecorder>();
builder.Services.AddSingleton<SecretRedactor>();
builder.Services.AddSingleton(TimeProvider.System);

// The durable store is optional, and its absence is a supported configuration
// rather than a degraded one: the demonstration runs on a laptop with no SQL
// Server, and everything except surviving a restart behaves identically. What
// changes is stated on the governance strip rather than left to be discovered.
string? counterConnection = builder.Configuration.GetConnectionString("Counter");

if (!string.IsNullOrWhiteSpace(counterConnection))
{
    // A factory rather than a scoped context: activity is written from a filter
    // that outlives the action's scope, and a context resolved per request would
    // already be disposed by the time the row is added.
    builder.Services.AddDbContextFactory<CounterContext>(options =>
        options.UseSqlServer(counterConnection, sql => sql.EnableRetryOnFailure()));
}

builder.Services.AddScoped<AvailabilityReader>();
builder.Services.AddScoped<PricingReader>();
builder.Services.AddScoped<CommitmentReader>();
builder.Services.AddScoped<AvailabilityService>();

builder.Services.AddControllers(options =>
{
    // Registered globally so no controller can forget it. An ERP failure
    // arriving as an empty success would be indistinguishable from "no stock",
    // and a representative would repeat that to a customer.
    options.Filters.Add<ErpProblemFilter>();

    // Every request is recorded against the person who made it, including the
    // ones that fail: a failure nobody recorded is indistinguishable from a
    // request nobody made.
    options.Filters.Add<ActivityRecordingFilter>();

    // Innermost, so the budget it imposes covers the action alone and the two
    // filters above still see -- and record -- the failure it raises.
    options.Filters.Add<ErpTimeoutFilter>();
});

builder.Services.AddProblemDetails();

builder.Services.AddCors(options =>
{
    // Named origins only. This application sends credentials, so a wildcard
    // origin would let any page a signed-in user visits drive it.
    string[] origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];

    options.AddDefaultPolicy(policy => policy
        .WithOrigins(origins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

WebApplication app = builder.Build();

// The built front end is served by the API, so one container carries both and
// the browser never has to reach two origins.
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseCors();
app.MapControllers();

// Anything the API does not answer is a front-end route, so the single-page
// application is returned and the client router takes it from there.
app.MapFallbackToFile("index.html");

// The schema is brought up to date and open sessions reloaded before anything
// is served, so a restart mid-call does not sign the person on the phone out.
using (IServiceScope scope = app.Services.CreateScope())
{
    IDbContextFactory<CounterContext>? contexts =
        scope.ServiceProvider.GetService<IDbContextFactory<CounterContext>>();

    if (contexts is not null)
    {
        await using CounterContext context = await contexts.CreateDbContextAsync();
        await context.Database.MigrateAsync();

        await app.Services.GetRequiredService<SessionStore>()
            .RestoreAsync(CancellationToken.None);
    }
}

// The catalogue is read once at startup so search answers instantly. It is built
// in the background: a slow ERP should delay search, not stop the application
// from starting and reporting why.
_ = Task.Run(async () =>
{
    CatalogueProjection catalogue = app.Services.GetRequiredService<CatalogueProjection>();
    ILogger<Program> logger = app.Services.GetRequiredService<ILogger<Program>>();

    try
    {
        await catalogue.BuildAsync(CancellationToken.None);
    }
#pragma warning disable CA1031 // Startup must survive any failure to read the catalogue.
    catch (Exception error)
#pragma warning restore CA1031
    {
        // Deliberately broad. Whatever goes wrong reading the catalogue, the
        // application must still start: an operator can then see the logged
        // reason and retry the build, where a process that exited silently
        // leaves them with nothing to read.
        logger.LogError(error, "The catalogue could not be read at startup");
    }
});

app.Run();

/// <summary>
/// Named so the integration tests can build a host from this application.
/// </summary>
public partial class Program;
