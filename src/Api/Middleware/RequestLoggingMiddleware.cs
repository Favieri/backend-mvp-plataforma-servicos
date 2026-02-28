using System.Diagnostics;
using Serilog;
using Serilog.Context;

namespace Api.Middleware;

public sealed class RequestLoggingMiddleware(RequestDelegate next)
{
    private static readonly ILogger Logger = Log.ForContext<RequestLoggingMiddleware>();

    public async Task InvokeAsync(HttpContext context)
    {
        var request = context.Request;
        var correlationId = context.Items[CorrelationIdMiddleware.ItemKey]?.ToString();
        var origin = request.Headers.Origin.FirstOrDefault();
        var userAgent = request.Headers.UserAgent.FirstOrDefault();
        var stopwatch = Stopwatch.StartNew();

        using (LogContext.PushProperty("Method", request.Method))
        using (LogContext.PushProperty("RequestPath", request.Path.Value ?? string.Empty))
        {
            Logger.Information(
                "RequestStart {@Request}",
                new
                {
                    method = request.Method,
                    path = request.Path.Value,
                    query = request.QueryString.Value,
                    origin,
                    userAgent,
                    correlationId
                });

            try
            {
                await next(context);
                stopwatch.Stop();

                using (LogContext.PushProperty("StatusCode", context.Response.StatusCode))
                {
                    Logger.Information(
                        "RequestEnd {@Request}",
                        new
                        {
                            method = request.Method,
                            path = request.Path.Value,
                            statusCode = context.Response.StatusCode,
                            elapsedMs = stopwatch.ElapsedMilliseconds,
                            correlationId
                        });
                }
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                using (LogContext.PushProperty("StatusCode", 500))
                {
                    Logger.Error(
                        ex,
                        "RequestUnhandledException {@Request}",
                        new
                        {
                            method = request.Method,
                            path = request.Path.Value,
                            query = request.QueryString.Value,
                            origin,
                            userAgent,
                            elapsedMs = stopwatch.ElapsedMilliseconds,
                            correlationId
                        });
                }

                throw;
            }
        }
    }
}
