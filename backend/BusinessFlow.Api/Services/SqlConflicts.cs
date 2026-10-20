using Microsoft.Data.SqlClient;

namespace BusinessFlow.Api.Services;

public static class SqlConflicts
{
    public static bool IsRetryable(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is SqlException { Number: 1205 or 1222 }) return true;
        return false;
    }
}
