# Backend .NET 8 (MVP Marketplace)

Novo backend portátil para **AWS Lambda + API Gateway HTTP API v2** e também executável como **container (ECS/Fargate)** sem mudanças de domínio.

## Estrutura

- `src/Api`: Minimal API (endpoints, middleware, swagger, problem details, CORS, Lambda hosting)
- `src/Application`: contratos, validação e regras
- `src/Domain`: entidades e regras puras
- `src/Infrastructure`: Dapper + Npgsql + integração Mercado Pago
- `tests/UnitTests`: regras críticas (transição/idempotência)
- `tests/IntegrationTests`: smoke test do `/health`
- `infra/sam/template.yaml`: IaC para Lambda + HTTP API v2

## Endpoints MVP migrados

- `GET /health`
- `POST /api/auth`
- `GET /api/orders`
- `POST /api/orders`
- `GET /api/orders/mine`
- `POST /api/orders/{id}/complete`
- `GET /api/appointments/mine`
- `POST /api/appointments`
- `PUT /api/appointments/{id}`
- `POST /api/payments/preference`
- `GET /api/payments/{orderId}`
- `POST /webhooks/mercadopago`
- `GET /api/wallet/balance`
- `GET /api/wallet/ledger`

## Banco e compatibilidade

- Leitura/escrita em tabelas existentes: `Order`, `Appointment`, `payment`, `ledger_entry`.
- Mantém formato JSON de rotas críticas próximo ao Next API atual.
- Não usa tipos de evento de Lambda no domínio/application.

## Variáveis de ambiente

Copie `.env.example` e configure:

- `DB_CONNECTION`
- `CORS_ALLOWED_ORIGINS`
- `MercadoPago__AccessToken`
- `MercadoPago__BaseUrl`

## Executar local

```bash
dotnet restore MarketplaceMvp.sln
dotnet build MarketplaceMvp.sln
dotnet test MarketplaceMvp.sln
dotnet run --project src/Api/Api.csproj
```

Swagger em `http://localhost:<porta>/swagger` (ambiente Development).

## Executar com SAM local

```bash
sam build -t infra/sam/template.yaml
sam local start-api -t infra/sam/template.yaml
```

## Deploy inicial (opcional)

```bash
sam deploy --guided --template-file infra/sam/template.yaml
```

## Container (futuro ECS/Fargate)

```bash
docker build -t marketplace-api-dotnet .
docker run --rm -p 8080:8080 --env-file .env marketplace-api-dotnet
```

## Webhook + idempotência

1. Registra payload bruto em `webhook_events` com chave única `(provider,event_id)`.
2. Se evento duplicado, responde `200 { duplicated: true }`.
3. Atualiza `payment` e `Order.status` de forma transacional.

> SQL esperado para idempotência:

```sql
create table if not exists webhook_events (
  provider text not null,
  event_id text not null,
  raw_payload text not null,
  status text not null,
  created_at timestamptz not null default now(),
  processed_at timestamptz null,
  primary key(provider, event_id)
);
```

## Compatibilidade com front (Next.js)

No front, adicione `API_BASE_URL` para alternar entre rotas do Next API e novo backend:

- Desenvolvimento: `http://localhost:5080`
- Produção: URL do API Gateway

## Segurança/autenticação

- Login compatível com hash bcrypt existente.
- JWT de Supabase: placeholder documentado em `.env.example` para ativação do middleware de validação (próxima iteração).

