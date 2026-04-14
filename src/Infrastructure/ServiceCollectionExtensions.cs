using Amazon.S3;
using Application.Abstractions;
using Application.Services;
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
        services.AddHttpClient();
        services.AddSingleton<IAmazonS3>(_ => new AmazonS3Client());
        services.Configure<DatabaseOptions>(o =>
        {
            o.ConnectionString = config["DB_CONNECTION"] ?? config.GetConnectionString("Default") ?? string.Empty;
            o.TimeoutSeconds = int.TryParse(config["DB_TIMEOUT_SECONDS"], out var timeout) ? timeout : 15;
            o.CommandTimeoutSeconds = int.TryParse(config["DB_COMMAND_TIMEOUT_SECONDS"], out var commandTimeout) ? commandTimeout : 15;
            // Default 5: appropriate for Lambda + Supabase transaction pooler (port 6543).
            // Each Lambda instance shares this single pool between EF Core and Dapper.
            // With N concurrent Lambda instances: 5 × N total connections to the pooler.
            o.MaximumPoolSize = int.TryParse(config["DB_MAX_POOL_SIZE"], out var maxPoolSize) ? maxPoolSize : 5;
            o.PoolerPort = int.TryParse(config["DB_POOLER_PORT"], out var poolerPort) ? poolerPort : 6543;
        });

        // Single shared NpgsqlDataSource used by both EF Core and Dapper (NpgsqlConnectionFactory).
        // This ensures one connection pool per Lambda instance instead of two, halving the
        // number of physical connections to the Supabase pooler.
        services.AddSingleton(sp =>
        {
            var env = sp.GetRequiredService<IHostEnvironment>();
            var rawCs = config["DB_CONNECTION"] ?? config.GetConnectionString("Default") ?? string.Empty;
            var timeout = int.TryParse(config["DB_TIMEOUT_SECONDS"], out var t) ? t : 15;
            var commandTimeout = int.TryParse(config["DB_COMMAND_TIMEOUT_SECONDS"], out var ct) ? ct : 15;
            var maxPoolSize = int.TryParse(config["DB_MAX_POOL_SIZE"], out var mp) ? mp : 5;
            var poolerPort = int.TryParse(config["DB_POOLER_PORT"], out var pp) ? pp : 6543;

            var csb = new NpgsqlConnectionStringBuilder(rawCs)
            {
                Timeout = timeout,
                CommandTimeout = commandTimeout,
                MaxPoolSize = maxPoolSize,
                Pooling = true,
                // NoResetOnClose: skip DISCARD ALL on connection return; the Supabase
                // transaction pooler handles state isolation between transactions itself.
                NoResetOnClose = true
            };

            // Supabase requires SSL on all connections (direct and pooler).
            if (NpgsqlConnectionFactory.IsSupabaseHost(csb))
            {
                csb.SslMode = SslMode.Require;
            }

            // In non-development environments, force the transaction pooler port (6543)
            // when the raw connection string still points to the direct port.
            if (!env.IsDevelopment() && NpgsqlConnectionFactory.ShouldUseSupabasePooler(csb))
            {
                csb.Port = poolerPort;
            }

            return new NpgsqlDataSourceBuilder(csb.ConnectionString).Build();
        });

        // Dapper services (email dedupe + payment modules) use the shared data source above.
        services.AddSingleton<IConnectionFactory, NpgsqlConnectionFactory>();

        services.AddDbContext<AppDbContext>((sp, options) =>
        {
            // Reuse the shared NpgsqlDataSource so EF Core and Dapper share one pool.
            var dataSource = sp.GetRequiredService<NpgsqlDataSource>();
            var commandTimeout = int.TryParse(config["DB_COMMAND_TIMEOUT_SECONDS"], out var ct) ? ct : 15;
            var env = sp.GetRequiredService<IHostEnvironment>();

            options.UseNpgsql(dataSource, npgsql =>
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

        // Social auth
        services.AddScoped<ISocialAuthService, Services.SocialAuthService>();

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
        services.AddScoped<IAvatarStorageRepository, AvatarStorageRepository>();

        // Service catalog (tiers + categories)
        services.AddScoped<IServiceCatalogRepository, ServiceCatalogRepository>();

        // Phase 1: proposals, timeline, payment orchestration
        services.AddScoped<IProposalRepository, ProposalRepository>();
        services.AddScoped<IOrderTimelineRepository, OrderTimelineRepository>();
        services.AddScoped<IPaymentOrchestrationService, Infrastructure.Services.PaymentOrchestrationService>();

        // Phase 2: chat transactional — anti-leak, contact masking, attachments
        services.AddSingleton<IAntiLeakDetectionService, AntiLeakDetectionService>();
        services.AddSingleton<IContactMaskingService, ContactMaskingService>();
        services.AddScoped<IMessageAttachmentRepository, MessageAttachmentRepository>();
        services.AddScoped<IAttachmentStorageRepository, AttachmentStorageRepository>();

        // Phase 3: dispute + expanded reviews
        services.AddScoped<IDisputeRepository, DisputeRepository>();
        services.AddHostedService<BackgroundJobs.ProposalExpirationJob>();

        // Phase 4: recurring billing + rebook
        services.AddScoped<IRecurringPlanRepository, RecurringPlanRepository>();
        services.AddSingleton<BackgroundJobs.RecurringBillingJob>();
        services.AddHostedService(sp => sp.GetRequiredService<BackgroundJobs.RecurringBillingJob>());

        // Phase 5: verification + trust metrics
        services.AddScoped<IProfessionalVerificationRepository, ProfessionalVerificationRepository>();
        services.AddScoped<ITrustMetricsService, Infrastructure.Services.TrustMetricsService>();
        services.AddSingleton<BackgroundJobs.TrustMetricsJob>();
        services.AddHostedService(sp => sp.GetRequiredService<BackgroundJobs.TrustMetricsJob>());

        // Email / notifications
        // TODO: CREDENTIALS - set SMTP_HOST, SMTP_PORT, SMTP_USER, SMTP_PASS, EMAIL_FROM env vars
        services.AddScoped<IEmailService, SmtpEmailService>();

        return services;
    }
}
