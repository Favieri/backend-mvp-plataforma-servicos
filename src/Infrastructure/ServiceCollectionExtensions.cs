using Application.Abstractions;
using Infrastructure.Data;
using Infrastructure.Payments;
using Infrastructure.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<DatabaseOptions>(o =>
        {
            o.ConnectionString = config["DB_CONNECTION"] ?? config.GetConnectionString("Default") ?? string.Empty;
            o.TimeoutSeconds = int.TryParse(config["DB_TIMEOUT_SECONDS"], out var timeout) ? timeout : 15;
        });

        services.AddSingleton<IConnectionFactory, NpgsqlConnectionFactory>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddHttpClient<IMercadoPagoClient, MercadoPagoClient>();
        return services;
    }
}
