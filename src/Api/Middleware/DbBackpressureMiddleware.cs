using Serilog;

namespace Api.Middleware;

public sealed class DbBackpressureMiddleware(RequestDelegate next, IConfiguration configuration)
{
    private static readonly ILogger Logger = Log.ForContext<DbBackpressureMiddleware>();
    private static readonly string[] ProtectedPaths = ["/professionals", "/api/orders", "/api/orders/mine"];
    private readonly SemaphoreSlim _semaphore = BuildSemaphore(configuration);

    public async Task InvokeAsync(HttpContext context)
    {
        if (!ShouldApply(context.Request.Path))
        {
            await next(context);
            return;
        }

        if (!await _semaphore.WaitAsync(0, context.RequestAborted))
        {
            const int retryAfterSeconds = 1;
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = retryAfterSeconds.ToString();

            Logger.Warning(
                "Throttling/Backpressure applied for {Method} {Path} (limit reached)",
                context.Request.Method,
                context.Request.Path.Value);

            await Results.Json(new
            {
                error = "Sistema temporariamente ocupado. Tente novamente.",
                retryAfterSeconds
            }).ExecuteAsync(context);
            return;
        }

        try
        {
            await next(context);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static SemaphoreSlim BuildSemaphore(IConfiguration configuration)
    {
        var max = int.TryParse(configuration["DB_MAX_CONCURRENT_REQUESTS"], out var configuredMax) ? configuredMax : 30;
        if (max < 1)
        {
            max = 1;
        }

        return new SemaphoreSlim(max, max);
    }

    private static bool ShouldApply(PathString path)
    {
        var value = path.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return ProtectedPaths.Any(protectedPath =>
            value.Equals(protectedPath, StringComparison.OrdinalIgnoreCase)
            || value.StartsWith($"{protectedPath}/", StringComparison.OrdinalIgnoreCase));
    }
}
