using Amazon.Lambda.AspNetCoreServer.Hosting;
using Api.Extensions;
using Api.Middleware;
using Application.Services;
using Infrastructure;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAWSLambdaHosting(LambdaEventSource.HttpApi);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddProblemDetails();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var corsAllowedOrigins = builder.Configuration["CORS_ALLOWED_ORIGINS"];
builder.Services.AddCors(options =>
{
    options.AddPolicy("default", policy => ConfigureCorsPolicy(policy, corsAllowedOrigins));
});

var app = builder.Build();

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

app.UseSerilogRequestLogging();
app.UseMiddleware<CorrelationIdMiddleware>();
app.UseRouting();
app.UseCors("default");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

var apiGroup = app.MapGroup(string.Empty).RequireCors("default");
apiGroup.MapMarketplaceEndpoints();

app.MapMethods("/{**path}", ["OPTIONS"], () => Results.NoContent())
    .RequireCors("default");

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
