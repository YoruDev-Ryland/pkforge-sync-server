using PKForge.Sync.Protocol;

namespace PKForge.Sync.Server.Http;

/// <summary>Thrown anywhere in a request to answer with an <see cref="ErrorResponse"/>.</summary>
public sealed class ApiException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;

    public static ApiException BadRequest(string code, string message) => new(StatusCodes.Status400BadRequest, code, message);
    public static ApiException NotFound(string message) => new(StatusCodes.Status404NotFound, ErrorCodes.NotFound, message);
}

public static class ApiErrors
{
    public static Task Write(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new ErrorResponse(code, message), ProtocolJson.Options);
    }
}

public sealed class ApiExceptionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (ApiException e) when (!context.Response.HasStarted)
        {
            await ApiErrors.Write(context, e.Status, e.Code, e.Message);
        }
        catch (BadHttpRequestException e) when (!context.Response.HasStarted)
        {
            await ApiErrors.Write(context, e.StatusCode, ErrorCodes.InvalidRequest, e.Message);
        }
    }
}
