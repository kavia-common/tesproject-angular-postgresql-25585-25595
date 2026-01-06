using Backend.Data;
using Backend.Models;
using Backend.Validation;
using Microsoft.AspNetCore.Http.HttpResults;
using NSwag.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// --------------------
// Configuration
// --------------------
// Env vars expected (set by orchestrator / deployment):
// - POSTGRES_URL, POSTGRES_USER, POSTGRES_PASSWORD, POSTGRES_DB, POSTGRES_PORT
// - FRONTEND_ORIGIN (optional; defaults to http://localhost:3000)
//
// NOTE: Database CLI connection info is documented in database/db_connection.txt and schema_notes.md.
// We do NOT hardcode connection strings in code.
var frontendOrigin = builder.Configuration["FRONTEND_ORIGIN"] ?? "http://localhost:3000";

string? pgHostOrUrl = builder.Configuration["POSTGRES_URL"];
string? pgUser = builder.Configuration["POSTGRES_USER"];
string? pgPassword = builder.Configuration["POSTGRES_PASSWORD"];
string? pgDb = builder.Configuration["POSTGRES_DB"];
string? pgPort = builder.Configuration["POSTGRES_PORT"];

if (string.IsNullOrWhiteSpace(pgHostOrUrl) ||
    string.IsNullOrWhiteSpace(pgUser) ||
    string.IsNullOrWhiteSpace(pgPassword) ||
    string.IsNullOrWhiteSpace(pgDb) ||
    string.IsNullOrWhiteSpace(pgPort))
{
    // Fail fast with a clear error if env isn't configured.
    throw new InvalidOperationException(
        "PostgreSQL env vars are not configured. Required: POSTGRES_URL, POSTGRES_USER, POSTGRES_PASSWORD, POSTGRES_DB, POSTGRES_PORT."
    );
}

// POSTGRES_URL might be a host or a full URL depending on the environment.
// The database container provides POSTGRES_URL and other parts separately; construct a robust connection string.
var host = pgHostOrUrl;
if (Uri.TryCreate(pgHostOrUrl, UriKind.Absolute, out var uri) && (uri.Scheme == "postgres" || uri.Scheme == "postgresql"))
{
    host = uri.Host;
    if (uri.Port > 0)
        pgPort = uri.Port.ToString();
    if (!string.IsNullOrWhiteSpace(uri.UserInfo))
    {
        // UserInfo may be user:pass; but we prefer explicit env vars and ignore parsing credentials.
    }

    if (!string.IsNullOrWhiteSpace(uri.AbsolutePath) && uri.AbsolutePath.Length > 1)
        pgDb = uri.AbsolutePath.TrimStart('/');
}

var csb = new Npgsql.NpgsqlConnectionStringBuilder
{
    Host = host,
    Port = int.TryParse(pgPort, out var p) ? p : 5432,
    Username = pgUser,
    Password = pgPassword,
    Database = pgDb,
    SslMode = Npgsql.SslMode.Disable,
};

// --------------------
// Services
// --------------------
builder.Services.AddSingleton(new ItemRepository(csb.ConnectionString));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApiDocument(cfg =>
{
    cfg.Title = "TesProject Backend API";
    cfg.Description = "Minimal .NET 8 API with PostgreSQL-backed CRUD for items.";
    cfg.Version = "1.0.0";
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendCors", policy =>
    {
        policy.WithOrigins(frontendOrigin)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// --------------------
// Middleware
// --------------------
app.UseCors("FrontendCors");

// Configure OpenAPI/Swagger
app.UseOpenApi();
app.UseSwaggerUi(config =>
{
    config.Path = "/docs";
});

// --------------------
// Endpoints
// --------------------

// PUBLIC_INTERFACE
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .WithName("Health")
   .WithSummary("Health check")
   .WithDescription("Returns basic health status for the API.");

// PUBLIC_INTERFACE
app.MapGet("/api/items", async Task<Results<Ok<object>, BadRequest<object>>> (
        int? page,
        int? pageSize,
        ItemRepository repo,
        CancellationToken ct) =>
    {
        var p = page ?? 1;
        var ps = pageSize ?? 20;
        if (p <= 0 || ps <= 0 || ps > 200)
        {
            return TypedResults.BadRequest((object)new { error = "Invalid pagination. page must be >= 1; pageSize must be 1..200." });
        }

        var (items, total) = await repo.ListAsync(p, ps, ct);
        return TypedResults.Ok((object)new { page = p, pageSize = ps, total, items });
    })
    .WithName("ListItems")
    .WithSummary("List items")
    .WithDescription("Lists items ordered by newest first with basic pagination via page and pageSize.")
    .Produces(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest);

// PUBLIC_INTERFACE
app.MapGet("/api/items/{id:guid}", async Task<Results<Ok<Item>, NotFound>> (
        Guid id,
        ItemRepository repo,
        CancellationToken ct) =>
    {
        var item = await repo.GetAsync(id, ct);
        return item is null ? TypedResults.NotFound() : TypedResults.Ok(item);
    })
    .WithName("GetItemById")
    .WithSummary("Get item by id")
    .WithDescription("Returns an item by UUID.")
    .Produces<Item>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status404NotFound);

// PUBLIC_INTERFACE
app.MapPost("/api/items", async Task<Results<Created<Item>, BadRequest<object>>> (
        CreateItemDto dto,
        ItemRepository repo,
        CancellationToken ct) =>
    {
        var errors = MinimalValidation.Validate(dto);
        if (errors.Count > 0)
        {
            return TypedResults.BadRequest((object)new { errors });
        }

        var created = await repo.CreateAsync(dto, ct);
        return TypedResults.Created($"/api/items/{created.Id}", created);
    })
    .WithName("CreateItem")
    .WithSummary("Create item")
    .WithDescription("Creates a new item. Title is required (1..200 chars).")
    .Produces<Item>(StatusCodes.Status201Created)
    .Produces(StatusCodes.Status400BadRequest);

// PUBLIC_INTERFACE
app.MapPut("/api/items/{id:guid}", async Task<Results<Ok<Item>, NotFound, BadRequest<object>>> (
        Guid id,
        UpdateItemDto dto,
        ItemRepository repo,
        CancellationToken ct) =>
    {
        var errors = MinimalValidation.Validate(dto);
        if (errors.Count > 0)
        {
            return TypedResults.BadRequest((object)new { errors });
        }

        var updated = await repo.UpdateAsync(id, dto, ct);
        return updated is null ? TypedResults.NotFound() : TypedResults.Ok(updated);
    })
    .WithName("UpdateItem")
    .WithSummary("Update item")
    .WithDescription("Updates an existing item by id. Title is required (1..200 chars).")
    .Produces<Item>(StatusCodes.Status200OK)
    .Produces(StatusCodes.Status400BadRequest)
    .Produces(StatusCodes.Status404NotFound);

// PUBLIC_INTERFACE
app.MapDelete("/api/items/{id:guid}", async Task<Results<NoContent, NotFound>> (
        Guid id,
        ItemRepository repo,
        CancellationToken ct) =>
    {
        var deleted = await repo.DeleteAsync(id, ct);
        return deleted ? TypedResults.NoContent() : TypedResults.NotFound();
    })
    .WithName("DeleteItem")
    .WithSummary("Delete item")
    .WithDescription("Deletes an item by id.")
    .Produces(StatusCodes.Status204NoContent)
    .Produces(StatusCodes.Status404NotFound);

app.Run();
