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
// rather than a degraded one: everything except surviving a restart behaves
// identically. What is lost is written to the log at startup, plainly enough for
// an operator to read.
//
// SQLite rather than a database server, and the reason is the deployment shape.
// This runs in a container that scales to zero when nobody is using it, so a
// database server would be the one thing that could not scale down with it --
// costing money continuously to hold a few thousand rows nobody is reading. A
// file on a mounted share costs nothing while idle and is there when the
// container comes back.
//
// The audit trail is the only durable thing here. No ERP data is stored, which
// is what lets the read-only claim be checked by comparing the ERP's own files
// before and after a run.
string? counterConnection = builder.Configuration.GetConnectionString("Counter");

if (!string.IsNullOrWhiteSpace(counterConnection))
{
    // A factory rather than a scoped context: activity is written from a filter
    // that outlives the action's scope, and a context resolved per request would
    // already be disposed by the time the row is added.
    builder.Services.AddDbContextFactory<CounterContext>(options =>
        options
            .UseSqlite(counterConnection, sqlite => sqlite.CommandTimeout(
                (int)NetworkShareSqlite.CommandBudget.TotalSeconds))
            .AddInterceptors(new NetworkShareSqlite()));
}

builder.Services.AddScoped<AvailabilityReader>();
builder.Services.AddScoped<PricingReader>();
builder.Services.AddScoped<CommitmentReader>();
builder.Services.AddScoped<AvailabilityService>();

// The request budget is enforced by ErpReader and nowhere else.
//
// There was a filter here that installed a budget as the request's cancellation
// token. It was removed because it did not work and could not: a resource filter
// wraps result execution, so its token was still the response's abort token
// while the response was being written -- and any request that legitimately took
// longer than the budget had its answer truncated. Measured, an unreachable ERP
// produced 200 with an empty body where it had produced a typed 504 before.
//
// An empty 200 is the single worst answer this application can give: the screen
// reads it as "no stock" and a representative repeats that to a customer. A
// filter meant to protect that distinction was destroying it.
//
// The reader races each ERP call against the budget and drops the connection
// when it expires, which bounds the wait without touching the response.
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

    ILogger<Program> startupLogger = app.Services.GetRequiredService<ILogger<Program>>();

    if (contexts is not null)
    {
        try
        {
            await using CounterContext context = await contexts.CreateDbContextAsync();

            // The directory, not just the file. On a freshly mounted share the
            // path the connection string names may not exist yet, and SQLite
            // reports that as "unable to open database file" -- which reads like
            // a permissions problem and is often not one.
            CounterDatabase.EnsureDirectoryExists(
                context.Database.GetConnectionString(), startupLogger);

            await context.Database.MigrateAsync();

            await app.Services.GetRequiredService<SessionStore>()
                .RestoreAsync(CancellationToken.None);
        }
#pragma warning disable CA1031 // The audit trail is optional; the application is not.
        catch (Exception error)
#pragma warning restore CA1031
        {
            // Deliberately broad, and this is the line that makes the sentence
            // above true rather than merely intended.
            //
            // The comment on the registration says the durable store is optional
            // and that its absence is supported. It was not: a share the
            // container could not write to threw here, unhandled, and killed the
            // process on startup -- so an application that answers stock
            // questions refused to start because it could not write down that
            // somebody had asked one. A claim in a comment that the code does not
            // keep is worse than no comment.
            //
            // Now it degrades. The activity panel still works from memory for
            // the length of a session, and the log says plainly what was lost.
            //
            // Said on the health endpoint as well as in the log, because until
            // this call the application went on reporting `isAuditDurable: true`
            // from here onwards. The claim was built from whether a database had
            // been configured rather than from whether anything could be stored
            // in it, so the one field a reviewer would check to find this
            // problem was the field concealing it.
            app.Services.GetRequiredService<ActivityRecorder>()
                .ReportDurableStoreUnavailable();

            startupLogger.LogError(
                error,
                "The audit trail could not be opened. The application is running, and the " +
                "record of who asked what will not survive a restart");
        }
    }
}

// The catalogue is read at startup so search answers instantly, in the background
// so that a slow ERP delays search rather than stopping the application from
// starting and reporting why.
//
// It retries, and that is not belt-and-braces. The ERP this reaches is itself a
// container that scales to zero, so the first attempt after a deployment
// routinely arrives while nothing is listening. Without a retry the application
// starts, fails once, and stays unsearchable until somebody happens to run a
// search — which fails too, slowly, and is the first anyone hears of it.
_ = Task.Run(async () =>
{
    CatalogueProjection catalogue = app.Services.GetRequiredService<CatalogueProjection>();
    ILogger<Program> logger = app.Services.GetRequiredService<ILogger<Program>>();

    // Backing off to a minute and staying there. The thing being waited for is
    // usually a container starting, which takes seconds; when it is something
    // worse, a minute between attempts keeps the log readable and stops a
    // failing dependency being hammered by its own client.
    TimeSpan[] delays =
    [
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
    ];

    for (int attempt = 0; ; attempt++)
    {
        try
        {
            await catalogue.BuildAsync(CancellationToken.None);

            if (attempt > 0)
            {
                logger.LogInformation(
                    "The catalogue was read on attempt {Attempt}", attempt + 1);
            }

            return;
        }
#pragma warning disable CA1031 // Startup must survive any failure to read the catalogue.
        catch (Exception error)
#pragma warning restore CA1031
        {
            // Deliberately broad. Whatever goes wrong reading the catalogue, the
            // application must still start: an operator can then see the logged
            // reason, and /health says plainly that search cannot answer yet.
            TimeSpan delay = delays[Math.Min(attempt, delays.Length - 1)];

            logger.LogWarning(
                error,
                "The catalogue could not be read (attempt {Attempt}). Trying again in {Delay}",
                attempt + 1,
                delay);

            await Task.Delay(delay);
        }
    }
});

app.Run();

/// <summary>
/// Named so the integration tests can build a host from this application.
/// </summary>
public partial class Program;
