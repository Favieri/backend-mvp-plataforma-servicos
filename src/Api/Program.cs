using Amazon.Lambda.AspNetCoreServer.Hosting;
using Api.Extensions;
using Api.Logging;
using Api.Middleware;
using Application.Services;
using Infrastructure;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Serilog;
using Serilog.Formatting.Json;

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

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();
app.UseMiddleware<DbBackpressureMiddleware>();
app.UseMiddleware<SupabaseAuthMiddleware>();

// CORS e Routing devem vir ANTES do ExceptionHandler para que respostas de erro
// (500, 404, etc.) incluam os headers CORS. Sem isso, o browser reporta "CORS error"
// em vez do erro real, dificultando diagnóstico e quebrando o fluxo do front-end.
app.UseRouting();
app.UseCors("default");

app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
        var problem = new ProblemDetails
        {
            Title = "Erro interno",
            Status = 500,
            Detail = error?.Message,
            Instance = context.Request.Path
        };
        await Results.Problem(problem.Detail, statusCode: problem.Status, title: problem.Title).ExecuteAsync(context);
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

public partial class Program;
