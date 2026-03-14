using Amazon.Lambda.AspNetCoreServer.Hosting;
using Api.Extensions;
using Api.Logging;
using Api.Middleware;
using Application.Services;
using Infrastructure;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Formatting.Json;
using Npgsql;

// Required for Npgsql timestamp compatibility: maps timestamptz columns to DateTime (not DateTimeOffset)
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

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

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<DbBackpressureMiddleware>();
app.UseMiddleware<SupabaseAuthMiddleware>();

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

app.Run();

static void ConfigureCorsPolicy(CorsPolicyBuilder policy, string? configuredOrigins)
{
    var origins = (configuredOrigins ?? "*")
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Where(origin => !string.IsNullOrWhiteSpace(origin))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    if (origins.Length == 0 || origins.Contains("*", StringComparer.Ordinal))
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders("x-correlation-id");
        return;
    }

    policy
        .WithOrigins(origins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .WithExposedHeaders("x-correlation-id");

    if (origins.Any(origin => origin.Contains('*')))
    {
        policy.SetIsOriginAllowedToAllowWildcardSubdomains();
    }
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

public partial class Program;
