namespace Shared.Common.Exceptions;

/// <summary>
/// Thrown when a resource existed but can permanently no longer be used — for
/// example a quiz attempt that expired before it was submitted. Maps to
/// HTTP 410 in ExceptionMiddleware, so a client can tell "start over" apart
/// from "try again" (409) without parsing the message.
/// </summary>
public class GoneException : Exception
{
    public GoneException(string message) : base(message)
    {
    }

    public GoneException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
