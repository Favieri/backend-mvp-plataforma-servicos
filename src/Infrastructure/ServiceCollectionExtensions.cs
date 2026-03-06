using Application.Abstractions;
using Infrastructure.Data;
using Infrastructure.Email;
using Infrastructure.Persistence;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

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

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            var rawConnectionString = config["DB_CONNECTION"] ?? config.GetConnectionString("Default") ?? string.Empty;
            var commandTimeout = int.TryParse(config["DB_COMMAND_TIMEOUT_SECONDS"], out var ct) ? ct : 15;
            var maxPoolSize = int.TryParse(config["DB_MAX_POOL_SIZE"], out var mp) ? mp : 30;
            var timeout = int.TryParse(config["DB_TIMEOUT_SECONDS"], out var t) ? t : 15;
            var poolerPort = int.TryParse(config["DB_POOLER_PORT"], out var pp) ? pp : 6543;

            var csb = new NpgsqlConnectionStringBuilder(rawConnectionString)
            {
                Timeout = timeout,
                CommandTimeout = commandTimeout,
                MaxPoolSize = maxPoolSize,
                Pooling = true,
                NoResetOnClose = true
            };

            var env = sp.GetRequiredService<IHostEnvironment>();
            if (!env.IsDevelopment() && NpgsqlConnectionFactory.ShouldUseSupabasePooler(csb))
            {
                csb.Port = poolerPort;
            }

            options.UseNpgsql(csb.ConnectionString, npgsql =>
            {
                npgsql.CommandTimeout(commandTimeout);
                npgsql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null);
            });

            if (env.IsDevelopment())
            {
                options.EnableDetailedErrors();
                // EnableSensitiveDataLogging only in development — never in production
                options.EnableSensitiveDataLogging();
            }
        });

        // Core repositories
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IProfessionalRepository, ProfessionalRepository>();
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<IAuthRepository, AuthRepository>();
        services.AddScoped<IProfessionalReadRepository, ProfessionalReadRepository>();

        // New repositories
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IProfessionalDetailRepository, ProfessionalDetailRepository>();
        services.AddScoped<IProfessionalServiceRepository, ProfessionalServiceRepository>();
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IReviewRepository, ReviewRepository>();
        services.AddScoped<IPortfolioRepository, PortfolioRepository>();
        services.AddScoped<IAvailabilityRepository, AvailabilityRepository>();
        services.AddScoped<IOrderIgnoreRepository, OrderIgnoreRepository>();

        // Email / notifications
        // TODO: CREDENTIALS - set SMTP_HOST, SMTP_PORT, SMTP_USER, SMTP_PASS, EMAIL_FROM env vars
        services.AddScoped<IEmailService, SmtpEmailService>();

        return services;
    }
}
