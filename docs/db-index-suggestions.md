# Sugestões de índices (Postgres)

Baseado nos filtros/ordenações atuais do backend.

> Importante: validar com `EXPLAIN (ANALYZE, BUFFERS)` em staging antes de aplicar em produção.

## 1) Listagem de pedidos por data

Query alvo: `OrderRepository.GetOrdersAsync` (`ORDER BY "createdAt" DESC`).

```sql
create index concurrently if not exists idx_order_created_at_desc
  on "Order" ("createdAt" desc);
```

## 2) Filtro por cliente + ordenação de pedidos

Query alvo: `OrderRepository.GetMineAsync` (`WHERE "clientId" = ... ORDER BY "createdAt" DESC`).

```sql
create index concurrently if not exists idx_order_client_created_at_desc
  on "Order" ("clientId", "createdAt" desc);
```

## 3) Filtro por serviço + ordenação

Query alvo: `GetOrdersAsync` quando `serviceId` é informado.

```sql
create index concurrently if not exists idx_order_service_created_at_desc
  on "Order" ("serviceId", "createdAt" desc);
```

## 4) Ignore por profissional

Subquery de exclusão em `ProfessionalOrderIgnore`:

```sql
create index concurrently if not exists idx_professional_order_ignore_prof_order
  on "ProfessionalOrderIgnore" ("professionalId", "orderId");
```

## 5) Zonas de profissional (filtro por zona)

Subqueries de `ProfessionalZone` + `User.zoneId`:

```sql
create index concurrently if not exists idx_professional_zone_prof_zone
  on "ProfessionalZone" ("professionalId", "zoneId");

create index concurrently if not exists idx_user_zone
  on "User" ("zoneId");
```

## 6) Appointment por cliente e início

Query alvo: `AppointmentRepository.GetByClientAsync`.

```sql
create index concurrently if not exists idx_appointment_client_starts_at_desc
  on "Appointment" ("clientId", "startsAt" desc);
```
