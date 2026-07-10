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
- `docs/api-contracts.md`: contratos de resposta dos endpoints principais

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
- `DB_TIMEOUT_SECONDS`
- `DB_COMMAND_TIMEOUT_SECONDS`
- `DB_MAX_POOL_SIZE`
- `DB_POOLER_PORT` *(default 6543 para Supabase pooler em produção)*
- `DB_MAX_CONCURRENT_REQUESTS` *(backpressure interno para rotas críticas)*

### CORS (backend como fonte da verdade)

`CORS_ALLOWED_ORIGINS` controla totalmente o CORS da API (Lambda hoje, portátil para ECS amanhã):

- `*` → permite qualquer origem (MVP/preview do Vercel).
- Lista por vírgula → ex.: `http://localhost:3000,https://app.jobeasy.com.br`.
- Wildcard de subdomínio → ex.: `https://*.vercel.app`.

A API responde preflight `OPTIONS` para qualquer path e expõe `x-correlation-id` para leitura no browser.

### Performance e proteção de banco (Lambda/ECS)

- `NpgsqlDataSource` singleton com pooling (evita custo de handshake por request).
- Backpressure em rotas críticas (`/professionals`, `/api/orders`, `/api/orders/mine`) com resposta `429` + `Retry-After` quando limite interno for atingido.
- Cache in-memory por instância (TTL curto):
  - `GET /professionals` (60s)
  - `GET /api/orders` (30s)
- Bypass de cache com header `Cache-Control: no-cache`.

> Em Lambda o cache é por instância quente; em ECS, o mesmo padrão funciona e pode ser evoluído para Redis se necessário.

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

## Validar artefato SAM (anti-regressão)

Valide automaticamente se o bundle gerado pelo SAM contém o assembly esperado da Lambda no root (`/var/task`).

```bash
./scripts/validate-sam-artifact.sh
```

Esse script já executa `sam build -t infra/sam/template.yaml` e falha com erro se `Api.dll` não existir em `.aws-sam/build/JobeasyApiFunction/`.

Alternativa via Makefile:

```bash
make sam-verify
```

Se o comando falhar, o deploy deve ser bloqueado até o `Handler` e o DLL publicado estarem alinhados.

## Deploy inicial (opcional)

```bash
sam deploy --guided --template-file infra/sam/template.yaml
```

## Provisioned Concurrency (desligado por padrão)

`infra/sam/template.yaml` já traz a estrutura completa de Provisioned Concurrency, controlada por um único parâmetro:

- `ProvisionedConcurrencyCount` (`Number`, `Default: 0`) — com `0`, a condição `EnableProvisionedConcurrency` resolve para `AWS::NoValue` e **nenhum** recurso de Provisioned Concurrency é criado (custo zero, comportamento atual inalterado).
- A função `JobeasyApiFunction` usa `AutoPublishAlias: live`, então todo deploy publica uma versão numerada e atualiza o alias `live`; o evento `ProxyEvent` do API Gateway é redirecionado automaticamente pelo SAM para o alias.

### Permissões IAM necessárias no role de deploy

O role assumido via OIDC pelo GitHub Actions (`arn:aws:iam::660573993178:role/GitHubActions-JobeasyApi-Deploy`) precisa, além das permissões Lambda já existentes, das ações abaixo — obrigatórias a partir do momento em que `AutoPublishAlias` está no template, mesmo com Provisioned Concurrency em `0`, porque o CloudFormation passa a publicar versão e gerenciar alias a cada deploy. Esse role **não é gerenciado por IaC neste repositório** (é configurado direto na conta AWS), então a policy precisa ser ajustada manualmente:

```json
{
  "Effect": "Allow",
  "Action": [
    "lambda:PublishVersion",
    "lambda:CreateAlias",
    "lambda:UpdateAlias",
    "lambda:GetAlias",
    "lambda:PutProvisionedConcurrencyConfig",
    "lambda:GetProvisionedConcurrencyConfig",
    "lambda:DeleteProvisionedConcurrencyConfig"
  ],
  "Resource": "arn:aws:lambda:sa-east-1:660573993178:function:*"
}
```

Ajuste o `Resource` para o padrão de nome real da função gerada pelo stack `jobeasy-api` (ex.: `jobeasy-api-JobeasyApiFunction-*`) se a policy atual já restringir por prefixo.

### Runbook — ativar Provisioned Concurrency no futuro

1. Confirme que o role de deploy tem as permissões acima (sem elas, o deploy falha com `AccessDenied` assim que `AutoPublishAlias` tentar publicar versão/alias).
2. Redeploy com `ProvisionedConcurrencyCount` maior que zero via `--parameter-overrides ParameterKey=ProvisionedConcurrencyCount,ParameterValue=5` (ajuste o valor).
3. No console Lambda, aguarde o status do Provisioned Concurrency mudar para `READY` (leva alguns minutos).
4. Rode o smoke test padrão (`GET /health`, `GET /professionals`) através do alias `live`.
5. Para reverter, redeploy com `ProvisionedConcurrencyCount=0` e confirme no console que o recurso foi removido (não fica "fantasma" cobrando).

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

### Correlation ID (observabilidade por request)

O backend lê `x-correlation-id` (case-insensitive) em toda request.

- Se vier no header, o valor é reaproveitado na resposta (`x-correlation-id`).
- Se não vier, o backend gera um GUID e devolve no header da resposta.
- Todos os logs estruturados do request carregam `CorrelationId`, `TraceId`, `SpanId`, `RequestPath` e `StatusCode` (quando disponíveis).

No front-end, gere e envie um correlation id por request:

```ts
const correlationId = crypto.randomUUID();

await fetch(`${API_BASE_URL}/api/orders`, {
  method: 'GET',
  headers: {
    'x-correlation-id': correlationId,
  },
});
```

Isso facilita rastrear a jornada completa (browser -> API Gateway/Lambda -> backend -> banco) no CloudWatch hoje e em ECS/Fargate amanhã.

## Segurança/autenticação

- Login compatível com hash bcrypt existente.
- JWT de Supabase: placeholder documentado em `.env.example` para ativação do middleware de validação (próxima iteração).



## Troubleshooting (CI/CD AWS SAM)

Se o `sam build` falhar com **"No .NET project found"**, confirme que o `CodeUri` está na raiz do repositório e que o `Makefile` publica explicitamente o projeto `src/Api/Api.csproj`.

Se o `sam build` falhar com **"Missing required parameter: --framework"**, o build do SAM está configurado para `makefile` no `template.yaml` e usa `Makefile` na raiz para executar `dotnet publish src/Api/Api.csproj -f net8.0`; mantenha `<TargetFramework>net8.0</TargetFramework>` no `src/Api/Api.csproj`.


## Secrets do GitHub Actions (deploy AWS)

Para o workflow de deploy funcionar, configure estes secrets no repositório:
`GitHub repo → Settings → Secrets and variables → Actions → New repository secret`

### Secrets Obrigatórios (deploy falha sem eles)

| Secret Name | Descrição | Exemplo |
|---|---|---|
| `DB_CONNECTION` | String de conexão PostgreSQL | `Host=aws-1-sa-east-1.pooler.supabase.com;Port=6543;...` |
| `JWT_SECRET` | Secret para assinar JWTs da aplicação | String aleatória segura (min 32 chars) |

### Secrets Opcionais (deploy continua sem eles, usando defaults)

| Secret Name | Descrição | Default se ausente |
|---|---|---|
| `MP_ACCESS_TOKEN` | Token Mercado Pago | `''` (pagamentos desabilitados) |
| `STORAGE_BUCKET_NAME` | Nome do bucket S3 | `jobeasy-storage-prod` |
| `GOOGLE_CLIENT_ID` | Google OAuth Client ID | `''` (login Google desabilitado) |
| `FACEBOOK_APP_ID` | Facebook App ID | `''` (login Facebook desabilitado) |
| `CORS_ALLOWED_ORIGINS` | Origens CORS permitidas | `*` |
| `SMTP_HOST` | Host SMTP para emails | `''` (emails desabilitados) |
| `SMTP_USER` | Usuário SMTP | `''` |
| `SMTP_PASS` | Senha SMTP | `''` |
| `ADMIN_BOOTSTRAP_SECRET` | Segredo para criar o primeiro admin via `POST /internal/admin/bootstrap` (header `X-Admin-Bootstrap-Secret`) | `''` (endpoint desabilitado). O endpoint também se autodesliga assim que já existir um admin no banco — a partir daí, novos admins são criados via `POST /users` autenticado como admin. |

> **Fallbacks aceitos:** `DATABASE_URL` é aceito como alternativa a `DB_CONNECTION`;
> `MERCADOPAGO_ACCESS_TOKEN` como alternativa a `MP_ACCESS_TOKEN`.


Se a Lambda falhar com **"Api.dll or binary /var/task/Api not found"**, verifique se o `sam build` está usando `BuildMethod: makefile` e se o alvo `build-JobeasyApiFunction` do `Makefile` (na raiz) publica para `$(ARTIFACTS_DIR)` (sem subpastas).
