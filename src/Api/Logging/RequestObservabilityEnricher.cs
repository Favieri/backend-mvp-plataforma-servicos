using System.Diagnostics;
using Serilog.Core;
using Serilog.Events;

namespace Api.Logging;

public sealed class RequestObservabilityEnricher(IHttpContextAccessor httpContextAccessor) : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity is not null)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("TraceId", activity.TraceId.ToString()));
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("SpanId", activity.SpanId.ToString()));
        }

        var context = httpContextAccessor.HttpContext;
        if (context is null)
        {
            return;
        }

        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("RequestPath", context.Request.Path.Value ?? string.Empty));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("StatusCode", context.Response.StatusCode));

        if (context.Items.TryGetValue(Middleware.CorrelationIdMiddleware.ItemKey, out var correlationId)
            && correlationId is not null)
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CorrelationId", correlationId.ToString()!));
        }
    }
}
