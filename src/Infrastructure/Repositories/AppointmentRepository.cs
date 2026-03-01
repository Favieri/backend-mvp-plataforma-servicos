using Application.Abstractions;
using Dapper;
using Domain.Entities;
using Infrastructure.Data;

namespace Infrastructure.Repositories;

public sealed class AppointmentRepository(IConnectionFactory factory) : IAppointmentRepository
{
    public async Task<IReadOnlyList<Appointment>> GetByClientAsync(string clientId, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = "select id AS \"Id\",\"professionalId\" AS \"ProfessionalId\",\"clientId\" AS \"ClientId\",\"serviceId\" AS \"ServiceId\",\"startsAt\" AS \"StartsAt\",\"endsAt\" AS \"EndsAt\",status AS \"Status\",location AS \"Location\",notes AS \"Notes\" from \"Appointment\" where \"clientId\"=@clientId order by \"startsAt\" desc";
        return (await conn.QueryAsync<Appointment>(new CommandDefinition(sql, new { clientId }, cancellationToken: ct))).ToList();
    }

    public async Task<Appointment> CreateAsync(Appointment appointment, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = "insert into \"Appointment\"(id,\"professionalId\",\"clientId\",\"serviceId\",\"startsAt\",\"endsAt\",status,location,notes,\"createdAt\",\"updatedAt\") values (gen_random_uuid()::text,@ProfessionalId,@ClientId,@ServiceId,@StartsAt,@EndsAt,@Status,@Location,@Notes,now(),now()) returning id AS \"Id\",\"professionalId\" AS \"ProfessionalId\",\"clientId\" AS \"ClientId\",\"serviceId\" AS \"ServiceId\",\"startsAt\" AS \"StartsAt\",\"endsAt\" AS \"EndsAt\",status AS \"Status\",location AS \"Location\",notes AS \"Notes\"";
        return await conn.QuerySingleAsync<Appointment>(new CommandDefinition(sql, appointment, cancellationToken: ct));
    }

    public async Task<Appointment?> UpdateStatusAsync(string id, string status, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = "update \"Appointment\" set status=@status,\"updatedAt\"=now() where id=@id returning id AS \"Id\",\"professionalId\" AS \"ProfessionalId\",\"clientId\" AS \"ClientId\",\"serviceId\" AS \"ServiceId\",\"startsAt\" AS \"StartsAt\",\"endsAt\" AS \"EndsAt\",status AS \"Status\",location AS \"Location\",notes AS \"Notes\"";
        return await conn.QuerySingleOrDefaultAsync<Appointment>(new CommandDefinition(sql, new { id, status }, cancellationToken: ct));
    }
}
