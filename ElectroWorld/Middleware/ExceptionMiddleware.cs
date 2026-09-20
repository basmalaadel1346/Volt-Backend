using System.Net;
using System.Text.Json;
using Shared.Common.Api;
using Shared.Common.Exceptions;
namespace ElectroWorld.Middleware
{
    public class ExceptionMiddleware
    {
        // Web defaults → camelCase, matching every other response in the API.
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;

        public ExceptionMiddleware(
            RequestDelegate next,
            ILogger<ExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // The client went away. Nobody is waiting for an answer and nothing
                // failed on our side, so this is neither an error nor a 500.
                _logger.LogInformation(
                    "Request {Method} {Path} was aborted by the client.",
                    context.Request.Method, context.Request.Path);
            }
            catch (Exception ex)
            {
                var statusCode = MapStatusCode(ex);

                // Only server faults are errors. A 4xx is the caller's mistake or an
                // expected business rule; logging it at Error buried the real ones.
                if (statusCode >= 500)
                    _logger.LogError(ex, "Unhandled exception occurred while processing the request.");
                else
                    _logger.LogWarning(
                        "Request {Method} {Path} failed with {StatusCode}: {Message}",
                        context.Request.Method, context.Request.Path, statusCode, ex.Message);

                if (context.Response.HasStarted)
                {
                    // Headers are already on the wire; writing a body now would throw.
                    _logger.LogWarning("The response had already started, so the error could not be written.");
                    throw;
                }

                await WriteErrorAsync(context, ex, statusCode);
            }
        }

        // ArgumentException appends " (Parameter 'dto')" to Message — an internal
        // parameter name the app would otherwise show the child.
        private static string ClientMessage(Exception exception) =>
            exception is ArgumentException { ParamName: { } param } argument
                ? argument.Message.Replace($" (Parameter '{param}')", string.Empty)
                : exception.Message;

        // InvalidOperationException is deliberately NOT mapped: EF Core and the
        // framework throw it for internal faults, and its message must never reach
        // the client. Business rules throw BusinessRuleException instead.
        //
        // There is no 403 for "another user's record" either: services report it
        // as KeyNotFoundException, so a 404 never confirms that an id exists.
        private static int MapStatusCode(Exception exception) => exception switch
        {
            KeyNotFoundException => (int)HttpStatusCode.NotFound,
            ConflictException => (int)HttpStatusCode.Conflict,
            GoneException => (int)HttpStatusCode.Gone,
            BusinessRuleException => (int)HttpStatusCode.BadRequest,
            ArgumentException => (int)HttpStatusCode.BadRequest,
            _ => (int)HttpStatusCode.InternalServerError
        };

        private static async Task WriteErrorAsync(
            HttpContext context,
            Exception exception,
            int statusCode)
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.StatusCode = statusCode;

            var message = statusCode == (int)HttpStatusCode.InternalServerError
                ? "حدث خطأ داخلي في الخادم"
                : ClientMessage(exception);

            // The shared envelope, so every failure in the API has one shape.
            // ⚠ BREAKING CHANGE for existing Assessment clients, which previously
            // received { statusCode, message }. Coordinate with the Flutter team.
            // To revert, restore the anonymous { statusCode, message } object.
            var response = ApiResponse<object>.Fail(message);

            await context.Response.WriteAsync(
                JsonSerializer.Serialize(response, JsonOptions));
        }
    }
}
