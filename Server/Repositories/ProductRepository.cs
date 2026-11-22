using Dapper;
using Server.Infrastructure;
using Server.Models;

namespace Server.Repositories;

public interface IProductRepository
{
    Task<IEnumerable<Product>> GetAllAsync();
    Task<Product?> GetByIdAsync(int id);
    Task<int> InsertAsync(ProductRequest p, DateTimeOffset now);
    Task UpdateAsync(int id, ProductRequest p);
    Task SetActiveAsync(int id, bool isActive);
    Task<IEnumerable<int>> GetMachineIdsUsingAsync(int productId);
}

public class ProductRepository : IProductRepository
{
    private const string Columns = "id, name, volume_ml, price, image_url, is_active, created_at";
    private readonly IDbConnectionFactory _db;

    public ProductRepository(IDbConnectionFactory db) => _db = db;

    public async Task<IEnumerable<Product>> GetAllAsync()
    {
        await using var conn = _db.Create();
        return await conn.QueryAsync<Product>($"SELECT {Columns} FROM products ORDER BY id");
    }

    public async Task<Product?> GetByIdAsync(int id)
    {
        await using var conn = _db.Create();
        return await conn.QuerySingleOrDefaultAsync<Product>(
            $"SELECT {Columns} FROM products WHERE id = @id", new { id });
    }

    public async Task<int> InsertAsync(ProductRequest p, DateTimeOffset now)
    {
        await using var conn = _db.Create();
        return await conn.ExecuteScalarAsync<int>(
            @"INSERT INTO products (name, volume_ml, price, image_url, created_at)
              VALUES (@Name, @VolumeMl, @Price, @ImageUrl, @now);
              SELECT LAST_INSERT_ID();",
            new { p.Name, p.VolumeMl, p.Price, p.ImageUrl, now });
    }

    public async Task UpdateAsync(int id, ProductRequest p)
    {
        await using var conn = _db.Create();
        await conn.ExecuteAsync(
            @"UPDATE products SET name = @Name, volume_ml = @VolumeMl, price = @Price, image_url = @ImageUrl
              WHERE id = @id",
            new { id, p.Name, p.VolumeMl, p.Price, p.ImageUrl });
    }

    public async Task SetActiveAsync(int id, bool isActive)
    {
        await using var conn = _db.Create();
        await conn.ExecuteAsync("UPDATE products SET is_active = @isActive WHERE id = @id", new { id, isActive });
    }

    public async Task<IEnumerable<int>> GetMachineIdsUsingAsync(int productId)
    {
        await using var conn = _db.Create();
        return await conn.QueryAsync<int>(
            "SELECT DISTINCT machine_id FROM trays WHERE product_id = @productId", new { productId });
    }
}