# Backend Performance Audit (Home)

## Problema
A home fazia 3 round-trips (`/professionals`, `/zones`, `/services`), aumentando latência e risco de throttling em Lambda/API Gateway.

## Melhorias implementadas

1. **Enriquecimento de `/professionals`**
   - Profissionais já retornam com `zones[]` e `services[]` (incluindo `icon` via join com `Service`).
   - Evita chamadas extras apenas para render.

2. **Endpoint agregado `/bootstrap`**
   - Retorna `professionals`, `zones` e `services` em uma única resposta.
   - Recomendado para Home.

3. **Cache in-memory com TTL curto**
   - `/zones`: 10 min
   - `/services`: 10 min
   - `/professionals`: 45s
   - `/bootstrap`: 45s
   - Suporte a bypass com `Cache-Control: no-cache`.

4. **Consultas sem N+1**
   - Busca base de profissionais em 1 query.
   - Busca serviços por lote usando `ANY(@professionalIds)`.
   - Busca zonas por lote usando `ANY(@professionalIds)`.
   - Agrupamento em memória por `ProfessionalId`.

5. **Filtros eficientes em `/professionals`**
   - `zoneId` via `EXISTS` em `ProfessionalZone`.
   - `serviceId` via `EXISTS` em `ProfessionalService`.

## Impacto esperado
- Menos round-trips na home.
- Menor tempo médio de resposta percebido.
- Menor pressão de concorrência no banco e na Lambda.

## Observabilidade mínima
- Logs de cache hit/miss para `/professionals` e `/bootstrap` em nível Debug.
- CorrelationId continua disponível via middleware.
