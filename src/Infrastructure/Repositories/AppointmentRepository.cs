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
        const string sql = "select id,professionalid as ProfessionalId,clientid as ClientId,serviceid as ServiceId,startsat as StartsAt,endsat as EndsAt,status,location,notes from \"Appointment\" where clientid=@clientId order by startsat desc";
        return (await conn.QueryAsync<Appointment>(new CommandDefinition(sql, new { clientId }, cancellationToken: ct))).ToList();
    }

    public async Task<Appointment> CreateAsync(Appointment appointment, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = "insert into \"Appointment\"(id,professionalid,clientid,serviceid,startsat,endsat,status,location,notes,createdat,updatedat) values (gen_random_uuid()::text,@ProfessionalId,@ClientId,@ServiceId,@StartsAt,@EndsAt,@Status,@Location,@Notes,now(),now()) returning id,professionalid as ProfessionalId,clientid as ClientId,serviceid as ServiceId,startsat as StartsAt,endsat as EndsAt,status,location,notes";
        return await conn.QuerySingleAsync<Appointment>(new CommandDefinition(sql, appointment, cancellationToken: ct));
    }

    public async Task<Appointment?> UpdateStatusAsync(string id, string status, CancellationToken ct)
    {
        using var conn = await factory.CreateOpenConnectionAsync(ct);
        const string sql = "update \"Appointment\" set status=@status,updatedat=now() where id=@id returning id,professionalid as ProfessionalId,clientid as ClientId,serviceid as ServiceId,startsat as StartsAt,endsat as EndsAt,status,location,notes";
        return await conn.QuerySingleOrDefaultAsync<Appointment>(new CommandDefinition(sql, new { id, status }, cancellationToken: ct));
    }
}
