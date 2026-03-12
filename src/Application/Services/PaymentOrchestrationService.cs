using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using Dapper;

namespace Application.Services;

/// <summary>
/// Bridges the main Order (PascalCase schema) with the financial module (snake_case schema)
/// via the marketplace_order_id column.
/// Orchestrates signal charge, balance hold, and release via Dapper/Npgsql directly.
/// </summary>
public sealed class PaymentOrchestrationService(
    IConfiguration config,
    ILogger<PaymentOrchestrationService> logger)
{
    private string ConnectionString =>
        config["DB_CONNECTION"] ?? config.GetConnectionString("Default") ?? string.Empty;

    /// <summary>
    /// Links the EF Order to the financial module by setting marketplace_order_id.
    /// Must be called after creating the Order row.
    /// </summary>
    public async Task LinkToFinancialOrderAsync(string orderId, string financialOrderId, CancellationToken ct)
    {
        try
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.ExecuteAsync(
                "UPDATE \"order\" SET marketplace_order_id = @marketplaceOrderId WHERE id = @financialOrderId",
                new { marketplaceOrderId = orderId, financialOrderId });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to link order {OrderId} to financial order {FinancialOrderId}", orderId, financialOrderId);
            throw;
        }
    }

    /// <summary>
    /// Returns the financial order IDs linked to a marketplace order.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetFinancialOrderIdsAsync(string orderId, CancellationToken ct)
    {
        try
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            var ids = await conn.QueryAsync<string>(
                "SELECT id::text FROM \"order\" WHERE marketplace_order_id = @orderId",
                new { orderId });
            return ids.ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch financial orders for {OrderId}", orderId);
            return [];
        }
    }

    /// <summary>
    /// Returns payment summary for a marketplace order (aggregated from financial module).
    /// </summary>
    public async Task<object?> GetPaymentSummaryAsync(string orderId, CancellationToken ct)
    {
        try
        {
            await using var conn = new NpgsqlConnection(ConnectionString);
            var rows = await conn.QueryAsync(
                @"SELECT p.id::text, p.amount_cents, p.status, p.payment_type, p.created_at
                  FROM payment p
                  JOIN ""order"" o ON o.id = p.order_id
                  WHERE o.marketplace_order_id = @orderId
                  ORDER BY p.created_at",
                new { orderId });
            return rows;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to fetch payment summary for {OrderId}", orderId);
            return null;
        }
    }
}
