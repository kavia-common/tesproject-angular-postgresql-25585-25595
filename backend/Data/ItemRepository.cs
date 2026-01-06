using Backend.Models;
using Dapper;
using Npgsql;

namespace Backend.Data;

public sealed class ItemRepository
{
    private readonly string _connectionString;

    public ItemRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    private NpgsqlConnection CreateConnection() => new(_connectionString);

    // PUBLIC_INTERFACE
    public async Task<(IReadOnlyList<Item> Items, int Total)> ListAsync(int page, int pageSize, CancellationToken ct)
    {
        // List items ordered by created_at DESC with simple pagination and a total count.
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var offset = (page - 1) * pageSize;

        const string sqlCount = "select count(*) from items;";
        const string sqlList = """
                               select
                                   id as "Id",
                                   title as "Title",
                                   description as "Description",
                                   created_at as "CreatedAt",
                                   updated_at as "UpdatedAt"
                               from items
                               order by created_at desc
                               limit @PageSize offset @Offset;
                               """;

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        var totalTask = conn.ExecuteScalarAsync<int>(new CommandDefinition(sqlCount, cancellationToken: ct));
        var itemsTask = conn.QueryAsync<Item>(new CommandDefinition(sqlList, new { PageSize = pageSize, Offset = offset }, cancellationToken: ct));

        await Task.WhenAll(totalTask, itemsTask);
        return (itemsTask.Result.AsList(), totalTask.Result);
    }

    // PUBLIC_INTERFACE
    public async Task<Item?> GetAsync(Guid id, CancellationToken ct)
    {
        // Get a single item by id, or null if not found.
        const string sql = """
                           select
                               id as "Id",
                               title as "Title",
                               description as "Description",
                               created_at as "CreatedAt",
                               updated_at as "UpdatedAt"
                           from items
                           where id = @Id;
                           """;

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        return await conn.QuerySingleOrDefaultAsync<Item>(new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
    }

    // PUBLIC_INTERFACE
    public async Task<Item> CreateAsync(CreateItemDto dto, CancellationToken ct)
    {
        // Create a new item and return the created row.
        const string sql = """
                           insert into items (title, description)
                           values (@Title, @Description)
                           returning
                               id as "Id",
                               title as "Title",
                               description as "Description",
                               created_at as "CreatedAt",
                               updated_at as "UpdatedAt";
                           """;

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        return await conn.QuerySingleAsync<Item>(
            new CommandDefinition(sql, new { Title = dto.Title, Description = dto.Description }, cancellationToken: ct)
        );
    }

    // PUBLIC_INTERFACE
    public async Task<Item?> UpdateAsync(Guid id, UpdateItemDto dto, CancellationToken ct)
    {
        // Update an existing item. Returns updated row or null if not found.
        const string sql = """
                           update items
                           set title = @Title,
                               description = @Description
                           where id = @Id
                           returning
                               id as "Id",
                               title as "Title",
                               description as "Description",
                               created_at as "CreatedAt",
                               updated_at as "UpdatedAt";
                           """;

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        return await conn.QuerySingleOrDefaultAsync<Item>(
            new CommandDefinition(sql, new { Id = id, Title = dto.Title, Description = dto.Description }, cancellationToken: ct)
        );
    }

    // PUBLIC_INTERFACE
    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        // Delete an item by id. Returns true if a row was deleted.
        const string sql = "delete from items where id = @Id;";

        await using var conn = CreateConnection();
        await conn.OpenAsync(ct);

        var affected = await conn.ExecuteAsync(new CommandDefinition(sql, new { Id = id }, cancellationToken: ct));
        return affected > 0;
    }
}
