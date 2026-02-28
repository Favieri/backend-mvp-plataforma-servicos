# Backend .NET 8 (Jobeasy)

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
- `GET /professionals` *(compat: alias para listagem usada pelo frontend legado)*
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

### CORS (backend como fonte da verdade)

`CORS_ALLOWED_ORIGINS` controla totalmente o CORS da API (Lambda hoje, portátil para ECS amanhã):

- `*` → permite qualquer origem (MVP/preview do Vercel).
- Lista por vírgula → ex.: `http://localhost:3000,https://app.jobeasy.com.br`.
- Wildcard de subdomínio → ex.: `https://*.vercel.app`.

A API responde preflight `OPTIONS` para qualquer path e expõe `x-correlation-id` para leitura no browser.

### Teste rápido de CORS (local)

```bash
# GET simples com Origin
curl -i -H "Origin: http://localhost:3000" http://localhost:5080/professionals

# Preflight OPTIONS
curl -i -X OPTIONS \
  -H "Origin: http://localhost:3000" \
  -H "Access-Control-Request-Method: GET" \
  -H "Access-Control-Request-Headers: Content-Type,Authorization,X-Correlation-Id" \
  http://localhost:5080/professionals
```

## Executar local

```bash
dotnet restore Jobeasy.sln
dotnet build Jobeasy.sln
dotnet test Jobeasy.sln
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
docker build -t jobeasy-api-dotnet .
docker run --rm -p 8080:8080 --env-file .env jobeasy-api-dotnet
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



## Troubleshooting (CI/CD AWS SAM)

Se o `sam build` falhar com **"No .NET project found"**, confirme que o `CodeUri` está na raiz do repositório e que o `Makefile` publica explicitamente o projeto `src/Api/Api.csproj`.

Se o `sam build` falhar com **"Missing required parameter: --framework"**, o build do SAM está configurado para `makefile` no `template.yaml` e usa `Makefile` na raiz para executar `dotnet publish src/Api/Api.csproj -f net8.0`; mantenha `<TargetFramework>net8.0</TargetFramework>` no `src/Api/Api.csproj`.


## Secrets do GitHub Actions (deploy AWS)

Para o workflow de deploy funcionar, configure estes secrets no repositório:

- `DB_CONNECTION` *(fallback: `DATABASE_URL`)*
- `MP_ACCESS_TOKEN` *(fallback: `MERCADOPAGO_ACCESS_TOKEN`)* **(opcional no deploy)**

> O workflow exige `DB_CONNECTION` (ou `DATABASE_URL`).
> Se token do Mercado Pago não estiver definido, o deploy continua usando o valor default do template SAM.


Se a Lambda falhar com **"Api.dll or binary /var/task/Api not found"**, verifique se o `sam build` está usando `BuildMethod: makefile`, se o alvo `build-JobeasyApiFunction` do `Makefile` (na raiz) publica para `$(ARTIFACTS_DIR)` (sem subpastas) **e se o deploy usa o template gerado** (`.aws-sam/build/template.yaml`) em vez do template fonte.
