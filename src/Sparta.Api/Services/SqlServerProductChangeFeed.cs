using Microsoft.Data.SqlClient;
using Sparta.Modules.Inventory.Sync;

namespace Sparta.Api.Services;

public sealed class SqlServerProductChangeFeed(IConfiguration configuration) : IProductChangeFeed
{
    private string ConnectionString => configuration.GetConnectionString("Inventory")
        ?? throw new InvalidOperationException("Missing connection string: Inventory");

    public async Task<long> CurrentVersionAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand("SELECT CHANGE_TRACKING_CURRENT_VERSION();", connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? 0 : Convert.ToInt64(value);
    }

    public async Task<long> MinimumValidVersionAsync(CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(
            "SELECT CHANGE_TRACKING_MIN_VALID_VERSION(OBJECT_ID(N'dbo.Products'));", connection);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        if (value is null or DBNull)
            throw new InvalidOperationException("SQL Change Tracking is not enabled for dbo.Products.");
        return Convert.ToInt64(value);
    }

    public async Task<IReadOnlyList<ProductChange>> ReadAsync(
        long fromVersion,
        long throughVersion,
        long afterVersion,
        int afterId,
        int take,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT TOP (@take)
                CT.Id,
                CT.SYS_CHANGE_VERSION,
                CT.SYS_CHANGE_OPERATION
            FROM CHANGETABLE(CHANGES dbo.Products, @fromVersion) AS CT
            WHERE CT.SYS_CHANGE_VERSION <= @throughVersion
              AND (CT.SYS_CHANGE_VERSION > @afterVersion
                   OR (CT.SYS_CHANGE_VERSION = @afterVersion AND CT.Id > @afterId))
            ORDER BY CT.SYS_CHANGE_VERSION, CT.Id;
            """;

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@take", take);
        command.Parameters.AddWithValue("@fromVersion", fromVersion);
        command.Parameters.AddWithValue("@throughVersion", throughVersion);
        command.Parameters.AddWithValue("@afterVersion", afterVersion);
        command.Parameters.AddWithValue("@afterId", afterId);

        var changes = new List<ProductChange>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var operation = reader.GetString(2) switch
            {
                "I" => ProductChangeOperation.Insert,
                "U" => ProductChangeOperation.Update,
                "D" => ProductChangeOperation.Delete,
                var value => throw new InvalidOperationException($"Unknown SQL Change Tracking operation '{value}'.")
            };
            changes.Add(new ProductChange(reader.GetInt32(0), reader.GetInt64(1), operation));
        }
        return changes;
    }
}
