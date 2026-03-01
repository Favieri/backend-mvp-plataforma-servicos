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
            o.CommandTimeoutSeconds = int.TryParse(config["DB_COMMAND_TIMEOUT_SECONDS"], out var commandTimeout) ? commandTimeout : 15;
            o.MaximumPoolSize = int.TryParse(config["DB_MAX_POOL_SIZE"], out var maxPoolSize) ? maxPoolSize : 30;
            o.PoolerPort = int.TryParse(config["DB_POOLER_PORT"], out var poolerPort) ? poolerPort : 6543;
        });

        services.AddSingleton<IConnectionFactory, NpgsqlConnectionFactory>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IProfessionalRepository, ProfessionalRepository>();
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddHttpClient<IMercadoPagoClient, MercadoPagoClient>();
        return services;
    }
}
