using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace AssessmentBL.Services;

/// <summary>
/// Recognises the SQL Server uniqueness errors the Assessment schema relies
/// on, so services can translate them into business conflicts instead of
/// letting a raw provider exception reach the client as a 500.
/// </summary>
internal static class DbUpdateExceptionExtensions
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;
    private const int DeadlockVictim = 1205;

    /// <summary>
    /// True when SQL Server chose this request as a deadlock victim, whether the
    /// provider surfaced it directly (a query) or wrapped it in a
    /// DbUpdateException (SaveChanges). The victim's transaction is already
    /// rolled back by the server.
    /// </summary>
    public static bool IsDeadlockVictim(this Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is SqlException { Number: DeadlockVictim })
                return true;
        }

        return false;
    }

    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is SqlException sql
        && sql.Number is UniqueIndexViolation or UniqueConstraintViolation;

    /// <summary>
    /// True when the violated index/constraint is the named one. SQL Server
    /// puts the constraint name in the error text, which is the only place
    /// the provider exposes it.
    /// </summary>
    public static bool IsUniqueViolationOf(this DbUpdateException exception, string constraintName) =>
        exception.IsUniqueViolation()
        && exception.InnerException!.Message.Contains(constraintName, StringComparison.OrdinalIgnoreCase);
}
