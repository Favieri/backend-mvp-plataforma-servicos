using Amazon.Lambda.AspNetCoreServer.Hosting;
using Api.Extensions;
using Api.Logging;
using Api.Middleware;
using Application.Services;
using Infrastructure;
using Infrastructure.Persistence;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Json;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;

// Required for Npgsql timestamp compatibility: maps timestamptz columns to DateTime (not DateTimeOffset)
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Fail-fast: required secrets must be present at startup
var missingVars = new List<string>();
if (string.IsNullOrWhiteSpace(builder.Configuration["JWT_SECRET"]))
    missingVars.Add("JWT_SECRET");
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("STORAGE_BUCKET_NAME")))
    missingVars.Add("STORAGE_BUCKET_NAME");
if (string.IsNullOrWhiteSpace(builder.Configuration["CORS_ALLOWED_ORIGINS"]))
    missingVars.Add("CORS_ALLOWED_ORIGINS");
if (missingVars.Count > 0)
    throw new InvalidOperationException(
        $"Required environment variables are missing: {string.Join(", ", missingVars)}");

builder.Logging.ClearProviders();
builder.Services.AddHttpContextAccessor();

builder.Host.UseSerilog((context, services, cfg) => cfg
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Routing", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore.Cors", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", Serilog.Events.LogEventLevel.Information)
    .Enrich.FromLogContext()
    .Enrich.With(services.GetRequiredService<RequestObservabilityEnricher>())
    .WriteTo.Console(new JsonFormatter()));

builder.Services.AddSingleton<RequestObservabilityEnricher>();
builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);
builder.Services.AddProblemDetails();
builder.Services.AddMemoryCache();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddMemoryCache();
builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("mp-callback", o =>
    {
        o.Window = TimeSpan.FromMinutes(1);
        o.PermitLimit = 10;
        o.QueueLimit = 0;
    });
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var corsAllowedOrigins = builder.Configuration["CORS_ALLOWED_ORIGINS"];
builder.Services.AddCors(options =>
{
    options.AddPolicy("default", policy => ConfigureCorsPolicy(policy, corsAllowedOrigins));
});

var app = builder.Build();

// CORS deve rodar cedo no pipeline para garantir headers em respostas geradas
// por middlewares anteriores aos endpoints (incluindo falhas 500).
app.UseRouting();
app.UseCors("default");
app.UseRateLimiter();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<DbBackpressureMiddleware>();
app.UseMiddleware<JwtAuthMiddleware>();

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var isDbConnectivityError = IsDatabaseConnectivityError(error);
        var statusCode = isDbConnectivityError ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status500InternalServerError;
        var title = isDbConnectivityError ? "Serviço de banco de dados indisponível" : "Erro interno";
        var detail = isDbConnectivityError
            ? "Não foi possível conectar ao banco de dados. Tente novamente em instantes."
            : error?.Message;

        if (error is not null)
        {
            LogException(context, error, statusCode);
        }

        await Results.Problem(detail, statusCode: statusCode, title: title).ExecuteAsync(context);
    });
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// OPTIONS preflight antes do grupo de endpoints para garantir resposta correta ao browser
app.MapMethods("/{**path}", ["OPTIONS"], () => Results.NoContent())
    .RequireCors("default");

var apiGroup = app.MapGroup(string.Empty).RequireCors("default");
apiGroup.MapMarketplaceEndpoints();
apiGroup.MapMpOAuthEndpoints();

// Aplica migrations pendentes automaticamente no startup.
// Garante que colunas como svcAddr* (AddAddressFields) e provider (AddSocialLoginFields)
// sejam criadas no banco antes de qualquer request ser processado.
// Todas as migrations são idempotentes (IF NOT EXISTS), mas se alguma falhar por qualquer
// motivo inesperado, o app continua — o schema já pode estar correto no banco.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        await db.Database.MigrateAsync();
    }
    catch (Exception migEx)
    {
        var migLogger = scope.ServiceProvider
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("MigrationStartup");
        var sqlState = FindNpgsqlSqlState(migEx);
        migLogger.LogError(migEx,
            "MigrateAsync falhou — continuando com schema existente. " +
            "SqlState={SqlState} Mensagem={Message}",
            sqlState ?? "n/a", migEx.Message);
    }
}

app.Run();

static void ConfigureCorsPolicy(CorsPolicyBuilder policy, string? configuredOrigins)
{
    // Sem fallback para wildcard ou localhost — ausência é capturada pelo fail-fast no startup
    var raw = configuredOrigins ?? "";
    var origins = raw
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (origins.Length == 0)
    {
        // Não deveria chegar aqui (fail-fast acima), mas por segurança recusa tudo
        policy.WithOrigins("https://placeholder-nenhuma-origem-configurada.invalid")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .WithExposedHeaders("x-correlation-id");
        return;
    }

    if (origins.Contains("*", StringComparer.Ordinal))
    {
        // Wildcard explícito — somente permitido se configurado intencionalmente via env var
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod()
              .WithExposedHeaders("x-correlation-id");
        return;
    }

    policy.WithOrigins(origins)
          .AllowAnyHeader()
          .AllowAnyMethod()
          .WithExposedHeaders("x-correlation-id");

    if (origins.Any(o => o.Contains('*')))
        policy.SetIsOriginAllowedToAllowWildcardSubdomains();
}

static bool IsDatabaseConnectivityError(Exception? ex)
{
    if (ex is null)
    {
        return false;
    }

    if (ex is NpgsqlException or TimeoutException)
    {
        return true;
    }

    if (ex is DbUpdateException dbUpdateEx)
    {
        return IsDatabaseConnectivityError(dbUpdateEx.InnerException);
    }

    return IsDatabaseConnectivityError(ex.InnerException);
}

static void LogException(HttpContext context, Exception error, int statusCode)
{
    var correlationId = context.Items[CorrelationIdMiddleware.ItemKey]?.ToString() ?? "unknown";
    var logger = Log.ForContext("SourceContext", "ExceptionHandler");

    // Extrai cadeia completa de inner exceptions para diagnóstico
    var innerChain = BuildInnerExceptionChain(error);

    // Extrai SqlState do NpgsqlException (código de erro PostgreSQL)
    var sqlState = FindNpgsqlSqlState(error);

    logger.Error(
        error,
        "UnhandledException {ExceptionType} {ExceptionMessage} CorrelationId={CorrelationId} " +
        "StatusCode={StatusCode} SqlState={SqlState} InnerExceptionChain={InnerExceptionChain}",
        error.GetType().FullName,
        error.Message,
        correlationId,
        statusCode,
        sqlState,
        innerChain);
}

static string BuildInnerExceptionChain(Exception? ex)
{
    var parts = new System.Text.StringBuilder();
    var current = ex?.InnerException;
    var depth = 0;
    while (current is not null && depth < 10)
    {
        if (parts.Length > 0) parts.Append(" → ");
        parts.Append($"[{current.GetType().Name}] {current.Message}");
        current = current.InnerException;
        depth++;
    }
    return parts.Length > 0 ? parts.ToString() : "(none)";
}

static string? FindNpgsqlSqlState(Exception? ex)
{
    var current = ex;
    while (current is not null)
    {
        if (current is NpgsqlException npgsqlEx)
        {
            return npgsqlEx.SqlState;
        }
        current = current.InnerException;
    }
    return null;
}

public partial class Program;
