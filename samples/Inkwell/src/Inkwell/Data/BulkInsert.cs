using System.Data;
using System.Data.Common;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Inkwell.Data;

/// <summary>
/// Multi-row <c>INSERT ... VALUES (...),(...)</c> for the demo seeder. Columns, value converters and provider parameter
/// types come from the EF model, so rows are stored exactly as SaveChanges would store them. Runs on the current
/// transaction of the context, 100 rows (and well under every provider's parameter limit) per statement.
/// </summary>
internal static class BulkInsert
{
    public const int RowsPerStatement = 100;

    sealed record Column(string Name, Func<object, object?> Get, RelationalTypeMapping? Mapping, bool Nullable, string? JsonType);

    public static async Task InsertAsync<T>(DbContext db, IEnumerable<T> rows, bool ignoreConflicts = false, CancellationToken ct = default) where T : class
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            foreach (var chunk in rows.Chunk(RowsPerStatement))
            {
                await using var command = Build(db, chunk, ignoreConflicts);
                command.Transaction = transaction;
                await command.ExecuteNonQueryAsync(ct);
            }
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    internal static DbCommand Build<T>(DbContext db, T[] chunk, bool ignoreConflicts) where T : class
    {
        var entity = db.Model.FindEntityType(typeof(T)) ?? throw new InvalidOperationException($"{typeof(T).Name} is not mapped.");
        var table = StoreObjectIdentifier.Create(entity, StoreObjectType.Table) ?? throw new InvalidOperationException($"{typeof(T).Name} has no table.");
        var columns = Columns(entity, table);
        var postgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
        var command = db.Database.GetDbConnection().CreateCommand();
        var sql = new StringBuilder($"INSERT INTO {(table.Schema is null ? "" : Quote(table.Schema) + ".")}{Quote(table.Name)} ({string.Join(", ", columns.Select(c => Quote(c.Name)))}) VALUES ");
        var n = 0;
        for (var r = 0; r < chunk.Length; r++)
        {
            sql.Append(r == 0 ? "(" : ",(");
            for (var c = 0; c < columns.Count; c++)
            {
                var column = columns[c];
                var name = "@p" + n++;
                var value = column.Get(chunk[r]);
                if (c > 0) sql.Append(',');
                if (column.JsonType is { } json)
                {
                    var p = command.CreateParameter();
                    p.ParameterName = name; p.DbType = DbType.String; p.Value = value ?? DBNull.Value;
                    command.Parameters.Add(p);
                    sql.Append(postgres ? $"CAST({name} AS {json})" : name);
                }
                else
                {
                    command.Parameters.Add(column.Mapping!.CreateParameter(command, name, value, column.Nullable));
                    sql.Append(name);
                }
            }
            sql.Append(')');
        }
        if (ignoreConflicts) sql.Append(" ON CONFLICT DO NOTHING");
        command.CommandText = sql.ToString();
        return command;
    }

    static List<Column> Columns(IEntityType root, StoreObjectIdentifier table)
    {
        var columns = new List<Column>();
        void Collect(IEntityType type, Func<object, object?> owner)
        {
            foreach (var p in type.GetProperties())
            {
                var name = p.GetColumnName(table);
                if (name is null || p.GetComputedColumnSql() is not null) continue;
                if (type != root && p.IsPrimaryKey()) continue; // an owned type shares the owner's key column
                if (p.IsShadowProperty() || (p.IsKey() && p.ValueGenerated == ValueGenerated.OnAdd && p.ClrType != typeof(Guid)))
                {
                    if (p.ValueGenerated != ValueGenerated.Never) continue; // store-generated
                    throw new InvalidOperationException($"Shadow property {type.DisplayName()}.{p.Name} cannot be bulk inserted.");
                }
                var get = p.GetGetter();
                columns.Add(new(name, e => get.GetClrValue(owner(e)!), p.GetRelationalTypeMapping(), p.IsNullable, null));
            }
            foreach (var nav in type.GetNavigations().Where(n => n.ForeignKey.IsOwnership && !n.IsOnDependent && !n.IsCollection))
            {
                var get = nav.GetGetter();
                object? Owned(object e) => get.GetClrValue(owner(e)!);
                if (nav.TargetEntityType.IsMappedToJson())
                    columns.Add(new(nav.TargetEntityType.GetContainerColumnName()!, e => Owned(e) is { } o ? JsonSerializer.Serialize(o, o.GetType()) : null, null, true, nav.TargetEntityType.GetContainerColumnType() ?? "jsonb"));
                else Collect(nav.TargetEntityType, Owned);
            }
        }
        Collect(root, e => e);
        return columns;
    }

    static string Quote(string identifier) => '"' + identifier.Replace("\"", "\"\"") + '"';
}
