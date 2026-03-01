using Amazon.Lambda.AspNetCoreServer.Hosting;
using Api.Extensions;
using Api.Middleware;
using Application.Services;
using Infrastructure;
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
builder.Services.AddMemoryCache();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var corsOrigins = builder.Configuration["CORS_ALLOWED_ORIGINS"] ?? "*";
builder.Services.AddCors(options =>
{
    options.AddPolicy("mvp", policy =>
    {
        if (corsOrigins == "*") policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
        else policy.WithOrigins(corsOrigins.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).AllowAnyHeader().AllowAnyMethod();
    });
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
app.UseCors("mvp");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapMarketplaceEndpoints();

app.Run();

public partial class Program;
