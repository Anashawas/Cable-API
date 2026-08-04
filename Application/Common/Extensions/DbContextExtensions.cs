using Microsoft.EntityFrameworkCore;

namespace Application.Common.Extensions;

public static class DbContextExtensions
{
    /// <summary>
    /// Reads an entity with UPDLOCK + ROWLOCK to prevent concurrent modifications.
    /// Must be used inside a BeginTransactionAsync scope.
    /// </summary>
    public static async Task<T?> FindWithLockAsync<T>(
        this DbSet<T> dbSet, string tableName, int id, CancellationToken ct = default) where T : class
    {
        return await dbSet
            .FromSqlRaw($"SELECT * FROM [{tableName}] WITH (UPDLOCK, ROWLOCK) WHERE Id = {{0}} AND IsDeleted = 0", id)
            .FirstOrDefaultAsync(ct);
    }

    public static async Task<List<T>> SqlQueryAsync<T>(this DbContext db, string sql, object[] parameters = null, CancellationToken cancellationToken = default)
        where T : class
    {
        if (parameters is null)
        {
            parameters = new object[] { };
        }

        if (typeof(T).GetProperties().Any())
        {
            return await db.Database
                .SqlQueryRaw<T>(sql, parameters)
                .ToListAsync(cancellationToken);
        }
        else
        {
            await db.Database.ExecuteSqlRawAsync(sql, parameters, cancellationToken);
            return default;
        }
    }
}

public class OutputParameter<TValue>
{
    private bool _valueSet = false;

    public TValue _value;

    public TValue Value
    {
        get
        {
            if (!_valueSet)
                throw new InvalidOperationException("Value not set.");

            return _value;
        }
    }

    public void SetValue(object value)
    {
        _valueSet = true;

        _value = null == value || Convert.IsDBNull(value) ? default(TValue) : (TValue)value;
    }
}