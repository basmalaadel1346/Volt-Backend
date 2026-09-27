namespace Shared.Common.Exceptions;

/// <summary>
/// Thrown when a request is well-formed but breaks a business rule — for
/// example activating a question with no correct option, or retrying an
/// attempt that has no wrong answers. Maps to HTTP 400 in ExceptionMiddleware,
/// and its Message is shown to the client, so it must be a user-facing sentence.
///
/// Use this, never InvalidOperationException, for business rules:
/// InvalidOperationException is also what EF Core and the framework throw for
/// internal faults, and those must stay a 500 with a generic message instead of
/// leaking their text to the client.
/// </summary>
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message)
    {
    }

    public BusinessRuleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
