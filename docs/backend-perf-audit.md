# Backend Performance Audit (.NET 8 Minimal API)

## Escopo
Auditoria inicial orientada para reduzir throttling em AWS Lambda com foco em:

- tempo médio/p95 por endpoint
- pressão no Postgres (pool/conexões)
- endpoints candidatos a cache
- pontos de backpressure

> Observação: nesta etapa não há série histórica consolidada exportada de CloudWatch/OTel no repositório. As prioridades abaixo usam análise de código + padrão esperado de tráfego MVP.

## Endpoints mais chamados (estimativa)

1. `GET /professionals`
   - Tela de descoberta/listagem no front tende a chamar com alta frequência.
   - Dependência principal: `OrderRepository.GetOrdersAsync()` (Postgres).
2. `GET /api/orders`
   - Variante de listagem com filtros.
   - Dependência principal: Postgres.
3. `GET /api/orders/mine`
   - Dashboard de usuário.
   - Dependência principal: Postgres.
4. `GET /health`
   - Checks de disponibilidade (ALB/API Gateway/monitoração).
   - Sem dependência de DB.

## Duração média/p95 (estado atual)

- **Média/p95 reais não estavam versionadas** (sem dump de métricas no repositório).
- A partir deste PR, logs estruturados de request (`RequestStart`/`RequestEnd`) incluem:
  - `elapsedMs`
  - `path`
  - `statusCode`
  - `CorrelationId`
- Com esses campos, é possível calcular p50/p95 no CloudWatch Logs Insights.

Exemplo de query (CloudWatch Logs Insights):

```sql
fields @timestamp, Request.path as path, Request.elapsedMs as elapsedMs
| filter @message like /RequestEnd/
| stats pct(elapsedMs, 50), pct(elapsedMs, 95), avg(elapsedMs), count() by path
| sort by pct(elapsedMs, 95) desc
```

## Dependências por endpoint

- Postgres (Supabase pooler):
  - `/professionals`, `/api/orders`, `/api/orders/mine`, appointments, wallet, auth
- Mercado Pago:
  - `POST /api/payments/preference`
  - `POST /webhooks/mercadopago`

## Principais achados

1. **Conexão DB por request sem data source compartilhado** (antes):
   - `NpgsqlConnection` era criado diretamente por request.
   - Corrigido para `NpgsqlDataSource` singleton com pooling.
2. **Falta de cache para listagens read-heavy**:
   - Adicionado cache in-memory com TTL curto para `/professionals` e `/api/orders`.
3. **Risco de saturação do pool em pico**:
   - Adicionado backpressure com `SemaphoreSlim` para rotas críticas e resposta `429` + `Retry-After`.
4. **Cancelamento incompleto em query específica**:
   - `GetMineAsync` sem `CancellationToken` no `QueryAsync`; corrigido.

## Queries mais sensíveis (revisão)

- `OrderRepository.GetOrdersAsync`
  - filtros por `serviceId`, `professionalId` (subquery de zona), `excludeProfessionalId`
  - ordenação por `createdAt desc`
- `OrderRepository.GetMineAsync`
  - filtro por `clientId`
  - ordenação por `createdAt desc`

## Plano recomendado pós-merge

1. Coletar 24h de métricas reais (`avg/p95`) após deploy.
2. Aplicar índices sugeridos em `docs/db-index-suggestions.md`.
3. Ajustar `DB_MAX_CONCURRENT_REQUESTS` e `DB_MAX_POOL_SIZE` com base no tráfego real.
4. Em ECS/Fargate, avaliar troca de cache local por Redis para consistência cross-instance.
