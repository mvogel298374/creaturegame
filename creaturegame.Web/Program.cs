using System.Text.Json;
using creaturegame.DB;
using creaturegame.Web.Battle;
using creaturegame.Web.Hubs;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHealthChecks();
builder
    .Services.AddSignalR()
    .AddJsonProtocol(opts =>
        opts.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    );
builder.Services.AddSingleton(
    DevModeOptions.Resolve(
        builder.Configuration.GetValue<bool?>("DevMode:Enabled"),
        builder.Environment.IsDevelopment()
    )
);
builder.Services.AddSingleton<EncounterFactory>();
builder.Services.AddSingleton<GameSessionManager>();

// DbContext factories — the battle runs on a background task outside any request
// scope, and read-only queries are short-lived, so a factory (pooled, explicitly
// created/disposed) fits better than a scoped context. Replaces inline `new()`.
builder.Services.AddDbContextFactory<PokemonDbContext>(opts =>
    opts.UseSqlite($"Data Source={DbPathHelper.GetDatabasePath("pokemon.db")}")
);
builder.Services.AddDbContextFactory<MovesDbContext>(opts =>
    opts.UseSqlite($"Data Source={DbPathHelper.GetDatabasePath("moves.db")}")
);
builder.Services.AddDbContextFactory<ItemsDbContext>(opts =>
    opts.UseSqlite($"Data Source={DbPathHelper.GetDatabasePath("items.db")}")
);
builder.Services.AddCors(opts =>
    opts.AddDefaultPolicy(policy =>
        policy
            .WithOrigins("http://localhost:5173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
    )
);

var app = builder.Build();

app.Use(
    (context, next) =>
    {
        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        return next(context);
    }
);
app.UseCors();

// Serve the built React SPA (published into wwwroot) as a single unit with the API:
// UseDefaultFiles rewrites "/" to index.html, UseStaticFiles serves the assets, and
// MapFallbackToFile sends any non-API/-hub path to index.html for client-side routing.
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();
app.MapHub<BattleHub>("/hubs/battle");
app.MapHealthChecks("/healthz");
app.Map("/api/{**rest}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

app.Run();
