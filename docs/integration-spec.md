# Backend Integration Specification — Jobeasy API

> **Versão:** 1.0 — Março 2026
> **Stack:** .NET 8 Minimal API · PostgreSQL (Supabase) · AWS Lambda + API Gateway HTTP API v2
> **Base URL (dev):** `http://localhost:5080`
> **Base URL (prod):** URL do API Gateway (variável `API_BASE_URL`)

---

## Sumário

1. [Convenções Gerais](#1-convenções-gerais)
2. [Autenticação](#2-autenticação)
3. [Marketplace (Fase 0)](#3-marketplace-fase-0)
4. [Usuários e Profissionais](#4-usuários-e-profissionais)
5. [Agendamentos](#5-agendamentos)
6. [Pedidos Transacionais — Fase 1](#6-pedidos-transacionais--fase-1)
7. [Propostas — Fase 1](#7-propostas--fase-1)
8. [Chat Transacional — Fase 2](#8-chat-transacional--fase-2)
9. [Disputas — Fase 3](#9-disputas--fase-3)
10. [Reviews Expandidas — Fase 3](#10-reviews-expandidas--fase-3)
11. [Recorrência e Recontratação — Fase 4](#11-recorrência-e-recontratação--fase-4)
12. [Verificação e Trust Metrics — Fase 5](#12-verificação-e-trust-metrics--fase-5)
13. [Jobs Internos (EventBridge)](#13-jobs-internos-eventbridge)
14. [Enumerações e Constantes](#14-enumerações-e-constantes)
15. [Máquina de Estados — Order](#15-máquina-de-estados--order)
16. [Erros Padrão](#16-erros-padrão)

---

## 1. Convenções Gerais

### Headers comuns

| Header | Tipo | Descrição |
|---|---|---|
| `Authorization` | `Bearer <jwt>` | JWT do Supabase. Obrigatório para endpoints autenticados |
| `x-correlation-id` | `string` (max 128) | ID de rastreamento por request. Gerado automaticamente se ausente. Ecoado na resposta |
| `Cache-Control: no-cache` | — | Bypassa cache in-memory nas rotas que cacheiam |

### Formato de datas

Todas as datas são **ISO 8601 UTC** (`2026-03-12T18:00:00Z`).

### IDs

Todos os IDs são `string` (GUID gerado pelo backend). Nunca assuma formato numérico.

### Preços

Todos os valores monetários estão em **centavos inteiros** (`int`). Exemplo: R$ 150,00 = `15000`.

### Paginação

A API não implementa paginação por query params por padrão. Filtros via query string são usados para limitar resultados por ator/status.

### Rate limiting / Backpressure

Rotas críticas de leitura retornam **429 Too Many Requests** quando o limite interno de conexões simultâneas for atingido. O header `Retry-After` indica o tempo de espera em segundos.

Rotas com backpressure:
- `GET /professionals`
- `GET /api/orders`
- `GET /api/orders/mine`

---

## 2. Autenticação

### POST /auth
Login com email e senha (bcrypt, legado). Não requer JWT.

**Request Body:**
```json
{
  "email": "usuario@exemplo.com",
  "senha": "senha123"
}
```

**Response 200:**
```json
{
  "id": "string",
  "name": "string",
  "email": "string",
  "role": "cliente | profissional | admin",
  "zoneId": "string | null"
}
```

**Response 401:**
```json
{ "error": "Credenciais inválidas" }
```

---

## 3. Marketplace (Fase 0)

### GET /health
Verificação de saúde.

**Response 200:**
```json
{ "status": "ok", "version": "v1" }
```

---

### GET /bootstrap
Endpoint único para a home. Carrega profissionais, zonas, serviços, categorias e tiers em uma única chamada. Cache de 45 segundos por instância.

**Query params (opcionais):** Nenhum.

**Response 200:**
```json
{
  "professionals": [
    {
      "id": "string",
      "userId": "string",
      "name": "string",
      "avatarUrl": "string | null",
      "rating": 4.8,
      "active": true,
      "completedJobsCount": 120,
      "availabilityText": "Hoje 14:00–18:00",
      "verificationStatus": "verified | pending | rejected | none",
      "trustScore": 95,
      "services": [
        {
          "id": "string",
          "serviceId": "string",
          "name": "Corte",
          "price": 45.0,
          "description": "string | null",
          "icon": "scissors"
        }
      ],
      "zones": [
        { "id": "string", "name": "Zona Sul" }
      ]
    }
  ],
  "zones": [
    { "id": "string", "name": "Centro" }
  ],
  "services": [
    { "id": "string", "name": "Encanador", "icon": "wrench" }
  ],
  "categories": [
    { "id": "string", "name": "Beleza", "icon": "scissors" }
  ],
  "tiers": [
    {
      "id": 1,
      "name": "Tier 1 — Agendamento Direto",
      "code": "tier1",
      "allowBookingDirect": true,
      "requiresProposal": false,
      "requiresChat": false,
      "allowedPriceFormats": ["fixed"],
      "defaultSignalPercent": 30,
      "maxInstallments": 1
    }
  ]
}
```

---

### GET /home/bootstrap
Alias que redireciona para `/bootstrap`.

---

### GET /professionals
Lista profissionais. Cache de 45 segundos. Suporta filtros avançados (Fase 5).

**Query params:**

| Param | Tipo | Descrição |
|---|---|---|
| `zoneId` | string | Filtra por zona |
| `serviceId` | string | Filtra por serviço |
| `verificationStatus` | `verified \| pending \| rejected` | Filtra por status de verificação (Fase 5) |
| `minRating` | double | Rating mínimo (ex: `4.5`) (Fase 5) |

**Response 200:** Array de `ProfessionalCard` (mesmo schema do `bootstrap.professionals`).

---

### GET /zones
Lista zonas ativas. Cache de 10 minutos.

**Response 200:**
```json
[{ "id": "string", "name": "Centro" }]
```

---

### GET /services
Lista serviços. Cache de 10 minutos.

**Query params:**

| Param | Tipo | Descrição |
|---|---|---|
| `categoryId` | string | Filtra por categoria |

**Response 200:**
```json
[{ "id": "string", "name": "Encanador", "icon": "wrench" }]
```

---

### GET /tiers
Lista tiers de contratação. Cache de 1 hora.

**Response 200:**
```json
[
  {
    "id": 1,
    "name": "string",
    "code": "tier1",
    "allowBookingDirect": true,
    "requiresProposal": false,
    "requiresChat": false,
    "allowedPriceFormats": ["fixed"],
    "defaultSignalPercent": 30,
    "maxInstallments": 1
  }
]
```

---

### GET /categories
Lista categorias de serviço. Cache de 1 hora.

**Response 200:**
```json
[{ "id": "string", "name": "Beleza", "icon": "scissors" }]
```

---

## 4. Usuários e Profissionais

### POST /users
Cria um novo usuário.

**Request Body:**
```json
{
  "name": "João Silva",
  "email": "joao@exemplo.com",
  "phone": "+5511999999999",
  "role": "cliente | profissional | admin",
  "senha": "senhaSegura123",
  "zoneId": "string (obrigatório para role=cliente)"
}
```

**Response 201:** Objeto `User` criado.

**Response 400:**
- `name, email, role e senha são obrigatórios`
- `role inválido`
- `zoneId é obrigatório para clientes`
- `Já existe um usuário com este email`
- `zoneId inválido (zona inexistente ou inativa)`

---

### POST /professionals
Cria perfil de profissional (vinculado a um usuário existente).

**Request Body:**
```json
{
  "userId": "string",
  "bio": "string | null",
  "zones": ["zoneId1", "zoneId2"],
  "active": true
}
```

**Response 201:** Objeto `Professional` criado.

**Response 400:**
- `userId é obrigatório`
- `Usuário não encontrado`
- `Usuário já possui cadastro de profissional`
- `Uma ou mais zonas são inválidas ou estão inativas`

---

### GET /professionals/{id}
Retorna detalhes completos de um profissional.

**Response 200:** Objeto `Professional` detalhado.

**Response 404:** `{ "error": "Profissional não encontrado." }`

---

### PUT /professionals/{id}
Atualiza dados do profissional.

**Request Body:**
```json
{
  "bio": "string | null",
  "active": true,
  "availabilityText": "Seg–Sex 9h–18h",
  "avatarUrl": "https://..."
}
```

**Response 200:** Objeto atualizado.

**Response 404:** `{ "error": "Profissional não encontrado." }`

---

### POST /upload-avatar
Upload de avatar do profissional (multipart/form-data).

**Form Fields:**
- `file`: arquivo de imagem (JPG, PNG, WEBP, máx 5MB)
- `professionalId`: string

**Response 200:**
```json
{ "ok": true, "avatarUrl": "https://..." }
```

**Response 400:**
- `Content-Type deve ser multipart/form-data`
- `Arquivo não enviado`
- `professionalId é obrigatório`
- `Formato inválido. Use JPG, PNG ou WEBP`
- `Arquivo excede 5MB`

**Response 404:** `{ "error": "Profissional não encontrado." }`

---

### GET /professionals/zones?professionalId={id}
Lista zonas de atendimento de um profissional.

**Response 200:** Array de zonas.

---

### PUT /professionals/zones
Atualiza zonas de atendimento.

**Request Body:**
```json
{
  "professionalId": "string",
  "zones": ["zoneId1", "zoneId2"]
}
```

**Response 200:** Profissional atualizado.

---

### GET /professional-services?professionalId={id}
Lista serviços oferecidos por um profissional.

**Query params:**

| Param | Tipo | Descrição |
|---|---|---|
| `professionalId` | string | Filtra por profissional |
| `serviceId` | string | Filtra por tipo de serviço |

**Response 200:** Array de `ProfessionalService`.

---

### POST /professional-services
Cria serviço oferecido pelo profissional.

**Request Body:**
```json
{
  "professionalId": "string",
  "serviceId": "string",
  "nomeServico": "Corte masculino",
  "preco": 45.00,
  "descricao": "string | null"
}
```

**Response 201:** `ProfessionalService` criado.

---

### GET /professional-services/{id}
Retorna um serviço específico.

**Response 200:** `ProfessionalService`.

**Response 404:** `{ "error": "Serviço não encontrado" }`

---

### PUT /professional-services/{id}
Atualiza serviço.

**Request Body:**
```json
{
  "nomeServico": "string | null",
  "preco": 0.0,
  "descricao": "string | null"
}
```

**Response 200:** Serviço atualizado.

---

### DELETE /professional-services/{id}
Remove serviço do profissional.

**Response 200:** `{ "ok": true }`

**Response 404:** `{ "error": "Serviço não encontrado" }`

---

## 5. Agendamentos

### GET /appointments?professionalId={id}
Lista agendamentos de um profissional.

**Query params:**

| Param | Tipo | Descrição |
|---|---|---|
| `professionalId` | string | **Obrigatório** |
| `status` | string | Filtra por status |
| `from` | datetime | Filtro de início |
| `to` | datetime | Filtro de fim |

**Response 200:** Array de `Appointment`.

---

### GET /appointments/mine?clientId={id}
Lista agendamentos do cliente.

**Response 200:** Array de `Appointment`.

---

### GET /appointments/slots?professionalId={id}&date=YYYY-MM-DD
Retorna slots disponíveis de um profissional para uma data.

**Response 200:** Array de slots disponíveis com horários UTC.

---

### POST /appointments
Cria um agendamento.

**Request Body:**
```json
{
  "professionalId": "string",
  "clientId": "string | null",
  "serviceId": "string | null",
  "startsAt": "2026-03-15T14:00:00Z",
  "endsAt": "2026-03-15T15:00:00Z",
  "location": "string | null",
  "notes": "string | null"
}
```

**Response 201:** `Appointment` criado.

---

### PUT /appointments/{id}
Atualiza agendamento.

**Response 200:** `Appointment` atualizado.

---

## 6. Pedidos Transacionais — Fase 1

> Pedidos transacionais introduzem o modelo por tiers, com preço decomposto em sinal (30%) + saldo (70%), proposta formal e máquina de estados com 18 statuses.

---

### POST /orders/booking
Cria pedido via booking direto (Tier 1). O tier deve ter `allowBookingDirect = true`.

**Request Body:**
```json
{
  "clientId": "string",
  "professionalId": "string",
  "serviceId": "string",
  "tierId": 1,
  "priceTotalCents": 15000,
  "signalCents": 4500,
  "balanceCents": 10500,
  "installments": 1,
  "paymentMethod": "pix | credit_card | boleto | null",
  "scope": "Descrição detalhada do escopo do serviço",
  "scheduledAt": "2026-03-20T10:00:00Z",
  "conversationId": "string | null",
  "addressId": "string | null",
  "description": "string | null"
}
```

**Response 201:** Objeto `Order` completo.

```json
{
  "id": "string",
  "clientId": "string",
  "professionalId": "string",
  "serviceId": "string",
  "tierId": 1,
  "origin": "booking_direct",
  "status": "awaiting_payment",
  "priceTotalCents": 15000,
  "signalCents": 4500,
  "balanceCents": 10500,
  "installments": 1,
  "paymentMethod": "pix",
  "scope": "string",
  "scheduledAt": "2026-03-20T10:00:00Z",
  "conversationId": "string | null",
  "addressId": "string | null",
  "description": "string | null",
  "proposalId": null,
  "recurringPlanId": null,
  "completedAt": null,
  "cancelledAt": null,
  "cancelledBy": null,
  "cancellationReason": null,
  "autoConfirmAt": null,
  "createdAt": "2026-03-12T18:00:00Z"
}
```

**Response 400/422:**
- `clientId é obrigatório`
- `professionalId é obrigatório`
- `serviceId é obrigatório`
- `priceTotalCents deve ser positivo`
- `tierId inválido`
- `Este tier não permite booking direto. Use proposta.`

---

### POST /orders/from-proposal/{proposalId}
Cria pedido a partir de uma proposta aceita (Tier 2/3). O sinal é calculado automaticamente como 30% do total.

**Path param:** `proposalId` — ID da proposta no status `sent` ou `negotiating`.

**Request Body:**
```json
{
  "clientId": "string",
  "installments": 1,
  "paymentMethod": "pix | credit_card | null",
  "addressId": "string | null"
}
```

**Response 201:** Objeto `Order` completo (mesmo schema do booking).

**Response 403:** `{ "error": "Não autorizado" }`

**Response 404:** `{ "error": "Proposta não encontrada" }`

**Response 422:**
- `Proposta em status '...' não pode ser aceita`
- `Proposta expirada`

---

### PUT /orders/{id}/status
Transição de status do pedido. A transição é validada pela máquina de estados conforme o papel do ator.

**Request Body:**
```json
{
  "actorId": "string",
  "actorRole": "client | professional | system | admin",
  "newStatus": "string (ver tabela de transições)",
  "reason": "string | null"
}
```

**Response 200:**
```json
{ "ok": true, "status": "novo_status" }
```

**Response 404:** `{ "error": "Pedido não encontrado" }`

**Response 422:**
```json
{
  "error": "Transição 'scheduled' → 'cancelled_client' não permitida para ator 'professional'"
}
```

---

### POST /orders/{id}/confirm-completion
Cliente confirma conclusão do serviço. Só funciona quando o pedido está em `awaiting_confirmation`.

**Query params:** `clientId=string`

**Response 200:**
```json
{ "ok": true, "status": "completed" }
```

**Response 400:** `{ "error": "clientId é obrigatório" }`

**Response 403:** `{ "error": "Não autorizado" }`

**Response 404:** `{ "error": "Pedido não encontrado" }`

**Response 422:** `{ "error": "Pedido não está aguardando confirmação" }`

---

### POST /orders/{id}/dispute
Abre disputa para um pedido (shortcut — use `POST /disputes` para disputa completa com evidências).

**Query params:**
- `clientId=string` (obrigatório)
- `reason=string` (opcional)

**Response 200:**
```json
{ "ok": true, "status": "disputed" }
```

**Response 422:** `{ "error": "Disputa só pode ser aberta quando pedido está em andamento ou aguardando confirmação" }`

---

### GET /orders/{id}/timeline
Retorna o histórico de eventos de um pedido (audit trail).

**Response 200:**
```json
[
  {
    "id": "string",
    "orderId": "string",
    "eventType": "order_created | status_changed_to_scheduled | ...",
    "actorId": "string | null",
    "actorRole": "client | professional | system | admin",
    "metadata": "{ json string } | null",
    "createdAt": "2026-03-12T18:00:00Z"
  }
]
```

---

### GET /orders/mine-v2
Lista pedidos do usuário por papel.

**Query params:**

| Param | Tipo | Descrição |
|---|---|---|
| `userId` | string | **Obrigatório** |
| `role` | `client \| professional` | Default: `client` |

**Response 200:** Array de `Order`.

---

### GET /orders?serviceId=&professionalId=
Lista pedidos (com cache de 30s). Rota legada de marketplace.

**Query params:**

| Param | Tipo | Descrição |
|---|---|---|
| `serviceId` | string | Filtra por serviço |
| `excludeProfessionalId` | string | Exclui pedidos de um profissional |
| `professionalId` | string | Filtra por profissional |
| `filterZones` | bool | Filtra por zonas do usuário |

---

### GET /orders/mine?clientId={id}
Lista pedidos do cliente (legado, sem role).

---

### POST /orders
Cria pedido simples (legado, sem tier/proposta).

**Request Body:**
```json
{
  "clientId": "string",
  "serviceId": "string",
  "description": "string | null",
  "location": "string | null",
  "date": "2026-03-20T10:00:00Z"
}
```

**Response 201:** `Order` com status `aberto`.

---

### POST /orders/{id}/complete
Conclui pedido (legado).

**Response 200:** `{ "ok": true }`

---

## 7. Propostas — Fase 1

> O fluxo de proposta permite ao profissional criar uma cotação formal para o cliente antes de gerar um pedido.

### Ciclo de vida da proposta

```
draft → sent → accepted (→ Order criado)
             → rejected
             → negotiating → sent
                           → accepted
                           → rejected
expired (automático via job)
```

---

### POST /proposals
Profissional cria uma proposta (rascunho).

**Request Body:**
```json
{
  "professionalId": "string",
  "clientId": "string",
  "serviceId": "string",
  "professionalServiceId": "string | null",
  "conversationId": "string | null",
  "scope": "Escopo detalhado do serviço",
  "includesDescription": "O que está incluso",
  "excludesDescription": "O que não está incluso",
  "priceTotalCents": 25000,
  "priceByStage": "string JSON | null",
  "durationEstimate": "3 horas",
  "suggestedDatetime": "2026-03-25T09:00:00Z",
  "visitFeeCents": 0,
  "validUntil": "2026-03-20T23:59:59Z"
}
```

**Response 201:** `ProposalDto`

```json
{
  "id": "string",
  "orderId": "string | null",
  "professionalId": "string",
  "clientId": "string",
  "serviceId": "string",
  "professionalServiceId": "string | null",
  "conversationId": "string | null",
  "scope": "string",
  "includesDescription": "string | null",
  "excludesDescription": "string | null",
  "priceTotalCents": 25000,
  "priceByStage": "string | null",
  "durationEstimate": "string | null",
  "suggestedDatetime": "2026-03-25T09:00:00Z",
  "visitFeeCents": 0,
  "validUntil": "2026-03-20T23:59:59Z",
  "status": "draft",
  "rejectionReason": "string | null",
  "createdAt": "2026-03-12T18:00:00Z",
  "updatedAt": "2026-03-12T18:00:00Z"
}
```

**Response 400:**
- `professionalId é obrigatório`
- `clientId é obrigatório`
- `serviceId é obrigatório`
- `scope é obrigatório`
- `priceTotalCents deve ser positivo`
- `validUntil deve ser uma data futura válida`

---

### GET /proposals/{id}
Retorna proposta por ID.

**Response 200:** `ProposalDto`

**Response 404:** `{ "error": "Proposta não encontrada" }`

---

### GET /proposals
Lista propostas por conversa ou por usuário.

**Query params (um é obrigatório):**

| Param | Tipo | Descrição |
|---|---|---|
| `conversationId` | string | Lista propostas de uma conversa |
| `userId` | string | Lista propostas do usuário |
| `role` | `client \| professional` | Papel do usuário (default: `client`) |

**Response 200:** Array de `ProposalDto`.

**Response 400:** `{ "error": "conversationId ou userId é obrigatório" }`

---

### PUT /proposals/{id}/send
Profissional envia proposta ao cliente (muda de `draft`/`negotiating` para `sent`).

**Request Body:**
```json
{ "professionalId": "string" }
```

**Response 200:** `{ "ok": true, "status": "sent" }`

**Response 403:** `{ "error": "Não autorizado" }`

**Response 422:** `{ "error": "Proposta em status '...' não pode ser enviada" }`

---

### POST /proposals/{id}/accept
Cliente aceita proposta. Cria pedido automaticamente com sinal = 30% do total.

**Request Body:**
```json
{
  "clientId": "string",
  "paymentMethod": "pix | credit_card | null",
  "installments": 1
}
```

**Response 201:**
```json
{
  "ok": true,
  "orderId": "string",
  "order": { /* Order completo */ }
}
```

**Response 403:** `{ "error": "Não autorizado" }`

**Response 422:**
- `Proposta em status '...' não pode ser aceita`
- `Proposta expirada`

---

### POST /proposals/{id}/reject
Cliente rejeita proposta.

**Request Body:**
```json
{
  "clientId": "string",
  "reason": "string | null"
}
```

**Response 200:** `{ "ok": true, "status": "rejected" }`

**Response 403:** `{ "error": "Não autorizado" }`

**Response 422:** `{ "error": "Proposta em status '...' não pode ser rejeitada" }`

---

### POST /proposals/{id}/negotiate
Inicia negociação / contraproposta (cliente ou profissional).

**Request Body:**
```json
{
  "actorId": "string",
  "actorRole": "client | professional",
  "counterScope": "string | null",
  "counterPriceCents": 20000
}
```

**Response 200:** `{ "ok": true, "status": "negotiating" }`

**Response 403:** `{ "error": "Não autorizado" }`

**Response 422:** `{ "error": "Proposta em status '...' não pode entrar em negociação" }`

---

## 8. Chat Transacional — Fase 2

> ⚠️ Endpoint válido para marcar leitura é `POST /chat/read` (não existe `/messages/mark-read` nesta API).


### POST /conversations
Cria uma conversa entre cliente e profissional.

**Request Body:**
```json
{
  "clientId": "string",
  "professionalId": "string",
  "orderId": "string | null",
  "appointmentId": "string | null"
}
```

**Response 200:** `Conversation` criada (ou existente).

---

### GET /conversations?clientId=&professionalId=
Lista conversas de um usuário.

**Query params (um é obrigatório):**

| Param | Tipo | Descrição |
|---|---|---|
| `clientId` | string | Conversas do cliente |
| `professionalId` | string | Conversas do profissional |

**Response 200:** Array de `Conversation`.

---

### GET /conversations/{id}/actions?requestingUserId={id}
Retorna ações transacionais disponíveis na conversa para o usuário solicitante.

**Response 200:** objeto com flags de ações permitidas (ex.: aceitar proposta, negociar, sugerir horário).

---

### PATCH /conversations/{id}/status
Atualiza status da conversa.

**Request Body:**
```json
{ "status": "active | archived | flagged" }
```

**Response 200:** `{ "ok": true, "status": "active|archived|flagged" }`.

---

### POST /messages
Envia mensagem em uma conversa.

**Request Body:**
```json
{
  "conversationId": "string",
  "senderId": "string",
  "text": "string",
  "type": "text | system | proposal | schedule_suggestion | attachment | offer",
  "metadata": "{ json string } | null",
  "replyToId": "string | null"
}
```

**Response 200:** `Message` criada.

---

### GET /messages?conversationId={id}
Lista mensagens de uma conversa.

**Response 200:** Array de `Message`.

---

### POST /chat/read
Marca mensagens como lidas.

**Request Body:**
```json
{
  "conversationId": "string",
  "userId": "string"
}
```

**Response 200:** `{ "ok": true }`

---

### POST /messages/attachment
Envia anexo de chat (multipart/form-data).

**Form fields obrigatórios:**
- `conversationId`
- `senderId`
- `file`

**Response 200:** objeto com `message` (tipo `attachment`) e `attachment` (metadados/URL).

---

> Para mensagens de proposta e sugestão de horário, use `POST /messages` com `type` (`proposal` ou `schedule_suggestion`) e payload em `metadata`.

---

## 9. Disputas — Fase 3

> Disputas são abertas pelo cliente quando há problema com o serviço. O profissional responde, e o admin pode resolver ou escalar.

### Ciclo de vida da disputa

```
opened → professional_responded → mediating → resolved
                                            → closed
       → resolved (direto pelo admin)
       → closed
```

---

### POST /disputes
Abre uma disputa (cliente). Pedido deve estar em `in_progress`, `awaiting_confirmation` ou `completed`. Apenas uma disputa por pedido.

**Request Body:**
```json
{
  "orderId": "string",
  "clientId": "string",
  "reason": "string",
  "description": "string | null",
  "evidenceUrls": ["https://...", "https://..."]
}
```

**Response 201:**
```json
{
  "ok": true,
  "id": "string",
  "orderId": "string",
  "status": "opened",
  "createdAt": "2026-03-12T18:00:00Z"
}
```

**Response 400:**
- `orderId é obrigatório`
- `clientId é obrigatório`
- `reason é obrigatório`

**Response 403:** `{ "error": "Pedido não pertence a este cliente" }`

**Response 404:** `{ "error": "Pedido não encontrado" }`

**Response 409:** `{ "error": "Já existe uma disputa aberta para este pedido" }`

**Response 422:**
- `Pedido no status '...' não pode ser contestado`
- `Pedido não possui profissional associado`

---

### GET /disputes/{id}
Retorna detalhes da disputa.

**Response 200:**
```json
{
  "id": "string",
  "orderId": "string",
  "clientId": "string",
  "professionalId": "string",
  "reason": "string",
  "description": "string | null",
  "evidenceUrls": ["https://..."],
  "professionalResponse": "string | null",
  "professionalEvidenceUrls": ["https://..."],
  "resolution": "string | null",
  "resolvedBy": "string | null",
  "refundAmountCents": null,
  "status": "opened | professional_responded | mediating | resolved | closed",
  "createdAt": "2026-03-12T18:00:00Z",
  "resolvedAt": "string | null"
}
```

**Response 404:** `{ "error": "Disputa não encontrada" }`

---

### GET /disputes?professionalId=&clientId=
Lista disputas por ator.

**Query params (um é obrigatório):**

| Param | Tipo | Descrição |
|---|---|---|
| `professionalId` | string | Disputas do profissional |
| `clientId` | string | Disputas do cliente |

**Response 200:** Array de disputas.

---

### PUT /disputes/{id}/respond
Profissional responde à disputa.

**Request Body:**
```json
{
  "professionalId": "string",
  "response": "string",
  "evidenceUrls": ["https://..."]
}
```

**Response 200:** `{ "ok": true, "id": "string", "status": "professional_responded" }`

**Response 403:** `{ "error": "Não autorizado" }`

**Response 422:** `{ "error": "Disputa no status '...' não aceita resposta" }`

---

### PUT /disputes/{id}/escalate
Escala a disputa para mediação (após resposta do profissional).

**Response 200:** `{ "ok": true, "id": "string", "status": "mediating" }`

**Response 422:** `{ "error": "Disputa no status '...' não pode ser escalada" }`

---

### PUT /disputes/{id}/resolve
Admin resolve a disputa.

**Request Body:**
```json
{
  "resolution": "string",
  "resolvedBy": "string (userId do admin)",
  "refundAmountCents": 15000
}
```

**Response 200:** `{ "ok": true, "id": "string", "status": "resolved" }`

**Response 422:** `{ "error": "Disputa já encerrada" }`

---

## 10. Reviews Expandidas — Fase 3

### POST /reviews
Cria review verificado (ligado a pedido concluído).

**Request Body:**
```json
{
  "professionalId": "string",
  "clientId": "string",
  "orderId": "string",
  "rating": 5,
  "comment": "string | null",
  "punctualityRating": 5,
  "qualityRating": 5,
  "communicationRating": 4,
  "cleanlinessRating": 5,
  "photoUrls": ["https://..."]
}
```

**Response 200:** `Review` criado.

---

### GET /reviews?professionalId={id}
Lista reviews de um profissional.

**Response 200:** Array de `Review`.

---

### GET /reviews/{id}
Retorna review por ID.

**Response 200:** `Review`.

---

### PATCH /reviews/{id}
Atualiza review (pelo cliente).

**Request Body:**
```json
{
  "rating": 4,
  "comment": "string | null"
}
```

**Response 200:** `Review` atualizado.

---

### POST /reviews/professional
Profissional avalia o cliente.

**Request Body:**
```json
{
  "professionalId": "string",
  "orderId": "string",
  "review": "string",
  "rating": 5
}
```

**Response 200:** `Review` de profissional para cliente.

---

## 11. Recorrência e Recontratação — Fase 4

> Permite ao cliente recontratar serviços de um pedido concluído, com opção de criar um plano recorrente com desconto.

---

### POST /orders/rebook/{orderId}
Recontrata serviço de um pedido concluído. Opcionalmente cria plano recorrente.

**Path param:** `orderId` — ID do pedido original (deve estar em status terminal `completed`, `evaluated`, `concluido` ou `auto_concluido`).

**Request Body:**
```json
{
  "clientId": "string",
  "scheduledAt": "2026-04-01T10:00:00Z",
  "paymentMethod": "pix | null",
  "installments": 1,
  "addressId": "string | null",
  "createRecurringPlan": true,
  "frequency": "weekly | biweekly | monthly",
  "discountPercent": 10
}
```

**Response 201:**
```json
{
  "order": {
    "id": "string",
    "origin": "recurring",
    "status": "awaiting_payment",
    "priceTotalCents": 13500,
    "signalCents": 4050,
    "balanceCents": 9450,
    "recurringPlanId": "string | null",
    /* ... demais campos de Order */
  },
  "recurringPlan": {
    "id": "string",
    "clientId": "string",
    "professionalId": "string",
    "serviceId": "string",
    "sourceOrderId": "string",
    "frequency": "weekly",
    "priceTotalCents": 15000,
    "discountPercent": 10,
    "effectivePriceCents": 13500,
    "paymentMethod": "pix",
    "scope": "string | null",
    "addressId": "string | null",
    "status": "active",
    "occurrenceCount": 0,
    "nextBillingAt": "2026-04-08T10:00:00Z",
    "createdAt": "2026-03-12T18:00:00Z"
  }
}
```
> Quando `createRecurringPlan = false`, o campo `recurringPlan` será `null`.

**Response 400:**
- `clientId é obrigatório`
- `discountPercent deve estar entre 0 e 100`
- `frequency inválido. Use: weekly, biweekly, monthly`

**Response 403:** `{ "error": "Não autorizado" }`

**Response 404:** `{ "error": "Pedido original não encontrado" }`

**Response 422:**
- `Recontratação só é permitida a partir de pedidos concluídos`
- `Pedido original sem profissional ou serviço associado`
- `Pedido original sem valor definido`

---

### GET /recurring-plans
Lista planos recorrentes.

**Query params (um é obrigatório):**

| Param | Tipo | Descrição |
|---|---|---|
| `clientId` | string | Planos do cliente |
| `professionalId` | string | Planos do profissional |

**Response 200:** Array de `RecurringPlan`.

---

### GET /recurring-plans/{id}
Retorna plano recorrente por ID.

**Response 200:** `RecurringPlan`.

**Response 404:** `{ "error": "Plano não encontrado" }`

---

### GET /recurring-plans/{id}/occurrences
Lista ocorrências (cobranças geradas) de um plano.

**Response 200:**
```json
[
  {
    "id": "string",
    "recurringPlanId": "string",
    "orderId": "string | null",
    "occurrenceNumber": 1,
    "scheduledDate": "2026-04-01T10:00:00Z",
    "status": "pending | order_created | paid | cancelled | failed",
    "createdAt": "2026-03-12T18:00:00Z"
  }
]
```

---

### PATCH /recurring-plans/{id}/pause
Pausa um plano recorrente ativo.

**Request Body:**
```json
{ "clientId": "string" }
```

**Response 200:** `{ "ok": true, "status": "paused" }`

**Response 403:** `{ "error": "Não autorizado" }`

**Response 422:** `{ "error": "Apenas planos ativos podem ser pausados" }`

---

### PATCH /recurring-plans/{id}/resume
Retoma um plano pausado.

**Request Body:**
```json
{ "clientId": "string" }
```

**Response 200:** `{ "ok": true, "status": "active" }`

**Response 422:** `{ "error": "Apenas planos pausados podem ser retomados" }`

---

### DELETE /recurring-plans/{id}
Cancela um plano recorrente.

**Request Body:**
```json
{ "clientId": "string" }
```

**Response 200:** `{ "ok": true, "status": "cancelled" }`

**Response 422:** `{ "error": "Plano já foi cancelado" }`

---

## 12. Verificação e Trust Metrics — Fase 5

> Profissionais podem enviar documentos para verificação. Admins revisam. O sistema calcula uma trust score agregada.

---

### POST /professionals/{id}/verification
Profissional envia documento para verificação.

**Path param:** `id` — ID do profissional.

**Request Body:**
```json
{
  "documentType": "rg | cnh | cpf | cnpj | diploma | crea | cau | crm | oab | other",
  "documentUrl": "https://..."
}
```

**Response 201:** `ProfessionalVerification` criado.

```json
{
  "id": "string",
  "professionalId": "string",
  "documentType": "rg",
  "documentUrl": "https://...",
  "status": "pending",
  "reviewedBy": null,
  "notes": null,
  "submittedAt": "2026-03-12T18:00:00Z",
  "reviewedAt": null
}
```

**Response 400:**
- `documentType é obrigatório`
- `documentUrl é obrigatório`
- `documentType inválido. Valores aceitos: rg, cnh, cpf, cnpj, diploma, crea, cau, crm, oab, other`

---

### GET /professionals/{id}/verification
Retorna verificação mais recente do profissional.

**Response 200:** `ProfessionalVerification`.

**Response 404:** `{ "error": "Nenhum documento de verificação encontrado." }`

---

### GET /professionals/{id}/verification/history
Retorna histórico completo de verificações do profissional.

**Response 200:** Array de `ProfessionalVerification`.

---

### PUT /professionals/verification/{verificationId}/review
Admin atualiza status de um documento (revisão).

**Path param:** `verificationId` — ID da verificação.

**Request Body:**
```json
{
  "status": "in_review | verified | rejected",
  "notes": "string (obrigatório quando status=rejected)"
}
```

**Response 200:** `ProfessionalVerification` atualizado.

**Response 400:**
- `status inválido. Valores aceitos: in_review, verified, rejected`
- `notes é obrigatório ao rejeitar um documento`

**Response 404:** `{ "error": "Verificação não encontrada." }`

---

### GET /admin/verification/pending
Lista todos os documentos aguardando revisão (fila do admin).

**Response 200:** Array de `ProfessionalVerification` com `status = pending`.

---

### GET /professionals/{id}/trust-metrics
Retorna métricas de confiança calculadas para o profissional.

**Response 200:** Objeto `Professional` com campos de trust metrics:

```json
{
  "id": "string",
  "userId": "string",
  "completionRate": 0.95,
  "responseRate": 0.88,
  "averageRating": 4.7,
  "verificationStatus": "verified | pending | rejected | none",
  "trustScore": 92,
  "badges": ["verified", "top_rated"],
  /* ... demais campos do profissional */
}
```

**Response 404:** `{ "error": "Profissional não encontrado." }`

---

## 13. Jobs Internos (EventBridge)

> Endpoints de uso exclusivo pelo EventBridge / chamadas internas. Protegidos por `X-Internal-Secret` header.

### Header de autenticação

```
X-Internal-Secret: <valor da variável INTERNAL_JOB_SECRET>
```
Se `INTERNAL_JOB_SECRET` não estiver configurada, a verificação é ignorada.

---

### POST /internal/jobs/auto-confirmation
Confirma automaticamente pedidos em `awaiting_confirmation` há mais de 72h.

**Response 200:**
```json
{ "processed": 5 }
```

---

### POST /internal/jobs/payment-timeout
Cancela pedidos em `awaiting_payment` há mais de 24h.

**Response 200:**
```json
{ "processed": 3 }
```

---

### POST /internal/jobs/recurring-billing
Gera novos pedidos para planos recorrentes com `next_billing_at <= agora`.

**Response 200:**
```json
{ "ok": true }
```

---

### POST /internal/jobs/trust-metrics?professionalId={id}
Recalcula trust metrics.

**Query params:**

| Param | Tipo | Descrição |
|---|---|---|
| `professionalId` | string | Recalcula apenas para este profissional. Se omitido, recalcula todos em background |

**Response 200 (profissional específico):**
```json
{ "ok": true, "scope": "single", "professionalId": "string" }
```

**Response 202 (todos):**
```json
{ "ok": true, "scope": "all", "message": "Recálculo iniciado em background." }
```

---

## 14. Enumerações e Constantes

### OrderStatus (status do pedido)

| Status | Descrição | Terminal |
|---|---|---|
| `aberto` | Legado: pedido aberto | — |
| `confirmado` | Legado: confirmado | — |
| `concluido` | Legado: concluído | ✓ |
| `auto_concluido` | Legado: auto-concluído | ✓ |
| `cancelado` | Legado: cancelado | ✓ |
| `draft` | Fase 1: rascunho | — |
| `proposal_sent` | Fase 1: proposta enviada | — |
| `awaiting_payment` | Fase 1: aguardando pagamento | — |
| `scheduled` | Fase 1: agendado | — |
| `in_transit` | Fase 1: profissional a caminho | — |
| `in_progress` | Fase 1: serviço em andamento | — |
| `awaiting_confirmation` | Fase 1: aguardando confirmação do cliente (72h) | — |
| `completed` | Fase 1: concluído pelo cliente | ✓ |
| `evaluated` | Fase 1: avaliado após conclusão | ✓ |
| `disputed` | Fase 1: em disputa | — |
| `cancelled_client` | Fase 1: cancelado pelo cliente | ✓ |
| `cancelled_professional` | Fase 1: cancelado pelo profissional | ✓ |
| `refunded` | Fase 1: reembolsado | ✓ |
| `rebooked` | Fase 4: recontraído | ✓ |

---

### ProposalStatus

| Status | Descrição |
|---|---|
| `draft` | Rascunho criado pelo profissional |
| `sent` | Enviado ao cliente |
| `accepted` | Aceito (gera Order) |
| `rejected` | Rejeitado pelo cliente |
| `negotiating` | Em negociação |
| `expired` | Expirou (via job) |

---

### RecurringFrequency

| Valor | Intervalo |
|---|---|
| `weekly` | 7 dias |
| `biweekly` | 14 dias |
| `monthly` | 30 dias |

---

### RecurringPlanStatus

| Status | Descrição |
|---|---|
| `active` | Plano ativo, gera pedidos automaticamente |
| `paused` | Pausado pelo cliente |
| `cancelled` | Cancelado (terminal) |

---

### DisputeStatus

| Status | Descrição | Terminal |
|---|---|---|
| `opened` | Disputa aberta | — |
| `professional_responded` | Profissional respondeu | — |
| `mediating` | Em mediação pela plataforma | — |
| `resolved` | Resolvido pelo admin | ✓ |
| `closed` | Encerrado | ✓ |

---

### ActorRole

| Valor | Descrição |
|---|---|
| `client` | Cliente |
| `professional` | Profissional |
| `system` | Sistema (jobs automatizados) |
| `admin` | Administrador |

---

### OrderOrigin

| Valor | Descrição |
|---|---|
| `booking_direct` | Booking direto (Tier 1) |
| `proposal_accepted` | Criado a partir de proposta (Tier 2/3) |
| `recurring` | Gerado por plano recorrente |
| `legacy` | Pedido legado sem tier |

---

### MessageType

| Valor | Descrição |
|---|---|
| `text` | Mensagem de texto simples |
| `system` | Mensagem automática do sistema |
| `proposal` | Proposta formal enviada via chat |
| `schedule_suggestion` | Sugestão de horário |
| `attachment` | Anexo de arquivo |
| `offer` | Oferta de serviço |

---

### DocumentType (verificação)

`rg` · `cnh` · `cpf` · `cnpj` · `diploma` · `crea` · `cau` · `crm` · `oab` · `other`

---

## 15. Máquina de Estados — Order

A tabela abaixo mostra as transições permitidas por `actorRole`:

| Status Atual | Novo Status | Atores Permitidos |
|---|---|---|
| `awaiting_payment` | `scheduled` | `client`, `system` |
| `awaiting_payment` | `cancelled_client` | `client` |
| `awaiting_payment` | `cancelled_professional` | `professional` |
| `scheduled` | `in_transit` | `professional` |
| `scheduled` | `in_progress` | `professional` |
| `scheduled` | `cancelled_client` | `client` |
| `scheduled` | `cancelled_professional` | `professional` |
| `in_transit` | `in_progress` | `professional` |
| `in_transit` | `cancelled_professional` | `professional` |
| `in_progress` | `awaiting_confirmation` | `professional` |
| `in_progress` | `disputed` | `client` |
| `in_progress` | `cancelled_professional` | `professional` |
| `awaiting_confirmation` | `completed` | `client`, `system` |
| `awaiting_confirmation` | `disputed` | `client` |
| `disputed` | `resolved` | `admin` |
| `disputed` | `refunded` | `admin` |
| `completed` | `evaluated` | `client` |
| `completed` | `rebooked` | `client` |
| Status legados | status legados | `client`, `professional` |

> **Regra auto-confirmação:** Pedidos em `awaiting_confirmation` são automaticamente marcados como `completed` após 72 horas (job `POST /internal/jobs/auto-confirmation`).

---

## 16. Erros Padrão

Todas as respostas de erro seguem o formato:

```json
{ "error": "Mensagem descritiva em português" }
```

Para erros de validação do FluentValidation:

```json
{
  "errors": {
    "campo": ["Mensagem de erro 1", "Mensagem de erro 2"]
  }
}
```

### Códigos HTTP utilizados

| Código | Significado |
|---|---|
| `200` | Sucesso |
| `201` | Criado |
| `202` | Aceito (processamento em background) |
| `400` | Requisição inválida / campos obrigatórios ausentes |
| `401` | Não autenticado |
| `403` | Não autorizado (ator errado) |
| `404` | Recurso não encontrado |
| `409` | Conflito (ex: disputa duplicada) |
| `422` | Entidade não processável (regra de negócio violada) |
| `429` | Muitas requisições (backpressure) |
| `500` | Erro interno |

---

## Apêndice: Variáveis de Ambiente

| Variável | Obrigatória | Descrição |
|---|---|---|
| `DB_CONNECTION` | ✓ | Connection string PostgreSQL |
| `SUPABASE_JWT_SIGNING_KEY` | ✓ | Chave de assinatura JWT |
| `SUPABASE_JWT_ISSUER` | ✓ | Issuer do token Supabase |
| `SUPABASE_JWT_AUDIENCE` | ✓ | Audience (`authenticated`) |
| `CORS_ALLOWED_ORIGINS` | — | Origens CORS (default `*`) |
| `INTERNAL_JOB_SECRET` | — | Secret para endpoints internos |
| `SMTP_HOST` | — | Host SMTP para e-mails |
| `SMTP_PORT` | — | Porta SMTP (default `587`) |
| `SMTP_USER` | — | Usuário SMTP |
| `SMTP_PASS` | — | Senha SMTP |
| `EMAIL_FROM` | — | Remetente dos e-mails |
| `APP_BASE_URL` | — | URL base do frontend (links em e-mails) |
| `DB_TIMEOUT_SECONDS` | — | Timeout de conexão (default `15`) |
| `DB_COMMAND_TIMEOUT_SECONDS` | — | Timeout de comando (default `15`) |
| `DB_MAX_POOL_SIZE` | — | Pool máximo de conexões (default `30`) |
| `DB_MAX_CONCURRENT_REQUESTS` | — | Limite de backpressure (default `30`) |
