using Application.Abstractions;
using Dapper;
using Domain.Entities;
using Infrastructure.Data;

namespace Infrastructure.Repositories;

public sealed class PaymentRepository(IConnectionFactory factory) : IPaymentRepository
{
    public async Task<Payment> UpsertAsync(Payment payment, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = @"insert into payment(id,order_id,gateway,gateway_ref,method,amount_cents,status,created_at,updated_at,paid_at)
values(@Id,@OrderId,@Gateway,@GatewayRef,@Method,@AmountCents,@Status,now(),now(),@PaidAt)
on conflict (gateway,gateway_ref) do update set status=excluded.status,updated_at=now(),paid_at=excluded.paid_at
returning id,order_id as OrderId,gateway,gateway_ref as GatewayRef,method,amount_cents as AmountCents,status,created_at as CreatedAt,paid_at as PaidAt";
        return await conn.QuerySingleAsync<Payment>(new CommandDefinition(sql, payment, cancellationToken: ct));
    }

    public async Task<Payment?> GetLatestByOrderAsync(string orderId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = "select id,order_id as OrderId,gateway,gateway_ref as GatewayRef,method,amount_cents as AmountCents,status,created_at as CreatedAt,paid_at as PaidAt from payment where order_id=@orderId order by created_at desc limit 1";
        return await conn.QuerySingleOrDefaultAsync<Payment>(new CommandDefinition(sql, new { orderId }, cancellationToken: ct));
    }

    public async Task<bool> TryStartWebhookProcessingAsync(string provider, string externalEventId, string rawPayload, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = @"insert into webhook_events(provider,event_id,raw_payload,status,created_at)
values(@provider,@externalEventId,@rawPayload,'processing',now())
on conflict(provider,event_id) do nothing";
        var affected = await conn.ExecuteAsync(new CommandDefinition(sql, new { provider, externalEventId, rawPayload }, cancellationToken: ct));
        return affected > 0;
    }

    public async Task MarkWebhookProcessedAsync(string provider, string externalEventId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = "update webhook_events set status='processed',processed_at=now() where provider=@provider and event_id=@externalEventId";
        await conn.ExecuteAsync(new CommandDefinition(sql, new { provider, externalEventId }, cancellationToken: ct));
    }

    public async Task ApplyPaymentStatusAsync(string gatewayRef, string status, DateTime? paidAt, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        await using var tx = await ((Npgsql.NpgsqlConnection)conn).BeginTransactionAsync(ct);
        const string paymentSql = "update payment set status=@status, paid_at=@paidAt, updated_at=now() where gateway_ref=@gatewayRef";
        await conn.ExecuteAsync(new CommandDefinition(paymentSql, new { status, paidAt, gatewayRef }, tx, cancellationToken: ct));
        const string orderSql = "update \"Order\" set status=case when @status='paid' then 'confirmado' else status end where id=(select order_id from payment where gateway_ref=@gatewayRef limit 1)";
        await conn.ExecuteAsync(new CommandDefinition(orderSql, new { status, gatewayRef }, tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
    }

    public async Task<int> GetWalletBalanceAsync(string professionalId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = "select coalesce(sum(amount_cents),0) from ledger_entry where professional_id=@professionalId";
        return await conn.ExecuteScalarAsync<int>(new CommandDefinition(sql, new { professionalId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<object>> GetLedgerAsync(string professionalId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = "select id,type,amount_cents as amountCents,created_at as createdAt,order_id as orderId,payment_id as paymentId,payout_item_id as payoutItemId from ledger_entry where professional_id=@professionalId order by created_at desc limit 100";
        return (await conn.QueryAsync(new CommandDefinition(sql, new { professionalId }, cancellationToken: ct))).ToList();
    }
}
