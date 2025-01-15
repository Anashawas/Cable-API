using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Infrastructrue.Persistence.Extensions;

public static class DbContextExtensions
{
    /// <summary>
    /// Gets new Object Ids for entities that are registered with GeoDatabase
    /// </summary>
    /// <param name="context">The database context</param>
    /// <param name="tableName">The table name</param>
    /// <param name="schemaName">The schema name</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns></returns>
    public static async Task<int> GenerateObjectId(this DbContext context, string tableName, string schemaName, CancellationToken cancellationToken = default)
    {
        var parameterReturnValue = new SqlParameter
        {
            ParameterName = "ObjectID",
            Size = -1,
            Direction = System.Data.ParameterDirection.Output,
            SqlDbType = System.Data.SqlDbType.Int,
        };

        var sqlParameters = new[]
        {
         new SqlParameter()
         {
             ParameterName="TableName",
             SqlDbType=System.Data.SqlDbType.NVarChar,
             Size=255,
             Value=tableName,
         },
            parameterReturnValue
        };

        await context.Database.ExecuteSqlRawAsync($"exec [{schemaName}].[OW_spGetNewObjectID] @TableName, @ObjectID OUTPUT"
            , sqlParameters, cancellationToken);

        return Convert.ToInt32(parameterReturnValue?.Value);
    }

    public static async Task<int> GetIdByMaxValue(this DbContext context, string columnName, string tableName, string schemaName, CancellationToken cancellationToken = default)
    {
        var parameterReturnValue = new SqlParameter
        {
            ParameterName = "Result",
            SqlDbType = System.Data.SqlDbType.Int,
            Direction = System.Data.ParameterDirection.Output
        };
        var sqlParameters = new[]
       {
         parameterReturnValue
     };
        await context.Database.ExecuteSqlRawAsync($"set @Result=(Select Isnull(max({columnName}),0)+1 from [{schemaName}].[{tableName}])", sqlParameters);
        return Convert.ToInt32(parameterReturnValue.Value);
        //return 
    }
}
