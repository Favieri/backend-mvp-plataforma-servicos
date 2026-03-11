# PRD Técnico de Impacto — Back-end Jobeasy

> Documento de análise técnica: gap entre o estado atual do back-end e o fluxo futuro definido pelo PRD de produto e blueprint funcional.
>
> **Escopo**: somente back-end (.NET 8, EF Core, PostgreSQL, integrações).
> **Objetivo**: mapear exatamente o que precisa ser alterado, criado ou removido no back-end para viabilizar o modelo híbrido por tiers.

---

## A. Resumo executivo técnico

### O que existe hoje

O back-end é uma **Minimal API .NET 8** (projeto `Api`) com arquitetura em camadas — `Domain` (records imutáveis), `Application` (interfaces + DTOs + validação), `Infrastructure` (repositórios EF Core + Dapper legado, Npgsql, e-mail SMTP). Deploy via AWS Lambda (SAM) ou container (Docker). Banco PostgreSQL no Supabase.

A aplicação cobre: autenticação por bcrypt/senha, CRUD de profissionais, serviços, zonas, pedidos simples (tabela `"Order"` com status textual), agendamento direto (tabela `"Appointment"`), chat básico (Conversation + Message), reviews, portfolio, disponibilidade por slots semanais, bloqueios de agenda, e um módulo financeiro separado (tabelas `order`, `payment`, `payable`, `ledger_entry`, `payoutbatch`, `payoutitem` — todas minúsculas com UUIDs, esquema snake_case distinto do esquema PascalCase da aplicação principal).

### O que muda

A evolução requer transformar a aplicação de um **diretório com chat e agendamento** em uma **plataforma de contratação protegida** com pedido como objeto central, taxonomia de serviços por tiers, propostas formais, chat transacional, pagamento obrigatório com sinal/saldo/parcelamento, disputas, recorrência e recontratação.

### Tamanho do impacto

- **~15 entidades de domínio novas ou substancialmente alteradas**
- **~12 tabelas novas no banco**
- **~8 tabelas existentes com alterações de colunas/constraints**
- **~25+ endpoints novos**
- **~10 endpoints existentes com alterações de contrato**
- **3 integrações novas** (gateway de pagamento completo, NLP anti-fuga, notificações push)
- **2 jobs/workers de background** (timeouts automáticos, cobranças recorrentes)
- **Refatoração do módulo financeiro** para unificar esquemas

---

## B. Arquitetura atual encontrada (back-end)

### B.1 Estrutura de projetos

```
src/
  Api/           → Minimal API, endpoints, middleware, CORS, Lambda hosting
  Application/   → Interfaces (I*Repository), DTOs, validação FluentValidation
  Domain/        → Entidades como records imutáveis (Appointment, Order, Professional, etc.)
  Infrastructure/→ AppDbContext (EF Core 8), repositórios, e-mail SMTP, NpgsqlConnectionFactory
```

### B.2 Entidades de domínio atuais

| Entidade | Tabela DB | PK tipo | Observações |
|---|---|---|---|
| `User` | `"User"` | `text` | Não contém `senha` na entity; campo acessado via SQL bruto em `AuthRepository` |
| `Professional` | `"Professional"` | `text` | Vinculado a `User` via `userId`. Contém config de agenda (`slotMinutes`, `leadTimeMinutes`, etc.) |
| `Service` | `"Service"` | `text` | Apenas `name` + `icon`. Sem conceito de tier/categoria |
| `ProfessionalService` | `"ProfessionalService"` | `text` | Vincula pro → service com `nomeServico`, `preco`, `descricao`. Sem modo de contratação, sem duração |
| `Zone` | `"Zone"` | `text` | Regiões de atendimento |
| `ProfessionalZone` | `"ProfessionalZone"` | composta | Associação pro ↔ zone |
| `Order` | `"Order"` | `text` | **Muito simplificado**: `clientId`, `serviceId`, `description`, `location`, `date`, `status`, `createdAt`. Status textual livre (`aberto`, `confirmado`, `concluido`, `cancelado`). **Sem vínculo com profissional, sem preço, sem proposta, sem pagamento.** |
| `Appointment` | `"Appointment"` | `text` | Agendamento separado do pedido. Status como PG enum. Sem vínculo formal com `Order` |
| `Conversation` | `"Conversation"` | `text` | `clientId`, `professionalId`, `orderId` (opcional). Chat puro sem ações transacionais |
| `Message` | `"Message"` | `text` | Texto simples, sem tipo de mensagem, sem anexos estruturados |
| `Review` | `"Review"` | `text` | Vinculado a `orderId` + `professionalId`. Nota única 1-5, sem categorias |
| `ProfessionalAvailability` | `"ProfessionalAvailability"` | `text` | Slots semanais (weekday, startMinutes, endMinutes) |
| `ProfessionalBlock` | `"ProfessionalBlock"` | `text` | Bloqueios pontuais de agenda |
| `ProfessionalPortfolio` | `"ProfessionalPortfolio"` | `text` | Imagens de portfólio |
| `ProfessionalOrderIgnore` | `"ProfessionalOrderIgnore"` | composta | Ignora pedidos específicos |

### B.3 Módulo financeiro separado (esquema snake_case)

Tabelas com UUIDs e convenção snake_case, desacopladas do domínio principal:

| Tabela | Colunas-chave | Uso atual |
|---|---|---|
| `order` (minúscula) | `customer_id` (text), `professional_id` (uuid), `amount_cents`, `status` | Pedido financeiro distinto de `"Order"` |
| `payment` | `order_id` (uuid → `order`), `gateway`, `gateway_ref`, `method` (enum), `amount_cents`, fees | Pagamentos Mercado Pago |
| `payable` | `professional_id`, `order_id`, `amount_cents`, `status` (enum), `hold_until` | Recebíveis do profissional |
| `ledger_entry` | `type` (enum), `order_id`, `payment_id`, `professional_id`, `amount_cents` | Extrato financeiro |
| `payoutbatch` / `payoutitem` | batch de repasse com status | Payout em lote |
| `professional` (minúscula) | `user_id`, `display_name`, `payout_method_id` | Profissional financeiro separado de `"Professional"` |
| `professional_payout_method` | PIX, banco, dados de conta | Métodos de recebimento |
| `webhook_events` | `provider`, `event_id`, `raw_payload` | Idempotência de webhooks (schema gap — referenciada no código mas ausente no dump) |

### B.4 Endpoints existentes (arquivo `ApiEndpoints.cs`)

**Públicos / Home**: `GET /health`, `GET /professionals`, `GET /zones`, `GET /services`, `GET /bootstrap`, `GET /home/bootstrap`

**Auth**: `POST /auth`

**Users**: `POST /users`

**Professionals**: `POST /professionals`, `GET /professionals/{id}`, `PUT /professionals/{id}`, `GET /professionals/zones`, `PUT /professionals/zones`, `POST /upload-avatar`

**Professional Services**: `GET /professional-services`, `POST /professional-services`, `GET /professional-services/{id}`, `PUT /professional-services/{id}`, `DELETE /professional-services/{id}`

**Orders**: `GET /orders`, `POST /orders`, `GET /orders/mine`, `POST /orders/{id}/complete`

**Appointments**: `GET /appointments`, `GET /appointments/mine`, `GET /appointments/slots`, `POST /appointments`, `PUT /appointments/{id}`

**Conversations / Messages**: `GET /conversations`, `POST /conversations`, `GET /messages`, `POST /messages`, `POST /chat/read`

**Reviews**: `GET /reviews`, `POST /reviews`, `GET /reviews/{id}`, `PATCH /reviews/{id}`, `GET /reviews/eligible-orders`

**Portfolio**: `GET /portfolio`, `POST /portfolio`, `GET /portfolio/{id}`, `PUT /portfolio/{id}`, `DELETE /portfolio/{id}`

**Availability / Blocks**: `GET /pro-availability/{id}`, `PUT|POST /pro-availability/{id}`, `GET /professional-blocks`, `POST /professional-blocks`

**Order Ignores**: `POST /order-ignores`, `DELETE /order-ignores`

### B.5 Integrações atuais

| Integração | Estado |
|---|---|
| Mercado Pago (pagamentos) | Webhook implementado, `payment` table com gateway_ref. Código de referência em `PaymentRepository` (Dapper). |
| E-mail SMTP | Implementado (`SmtpEmailService`), dry-run sem SMTP_HOST configurado. Templates de booking e chat message. |
| Supabase Storage | Upload de avatar via API REST (`AvatarStorageRepository`). |
| Push notifications | **Não existe** |
| NLP/detecção de contato | **Não existe** |
| Antifraude/verificação KYC | **Não existe** |
| Google Calendar sync | **Não existe** |

### B.6 Autenticação atual

Login por e-mail + senha (bcrypt). Sem OAuth/social login. Sem OTP por telefone. JWT do Supabase configurado no `.env` mas middleware de validação é placeholder (próxima iteração, conforme README).

---

## C. Gap analysis

### C.1 O que já existe e pode ser adaptado

| Componente atual | Adaptação necessária |
|---|---|
| `"Order"` (tabela + entity + repo) | Precisa de **expansão radical**: adicionar `professionalId`, `tierId`, `origin`, preço, sinal, saldo, parcelamento, endereço, escopo, ~14 novos status. Virar o objeto central. |
| `"Appointment"` | Pode ser mantido como sub-objeto do pedido. Vincular formalmente a `Order`. |
| `"Conversation"` + `"Message"` | Manter estrutura base. Adicionar `messageType` (text, proposal, action, system) em `Message`. Vincular conversation obrigatoriamente a `Order` quando houver. |
| `"ProfessionalService"` | Expandir com: `tierId`, `contractMode`, `durationMinutes`, `includesDescription`, `excludesDescription`, `materialIncluded`, `visitFee`, `minLeadTimeMinutes`. |
| `"Review"` | Expandir com categorias de nota (pontualidade, qualidade, comunicação, limpeza), fotos, double-blind flag. |
| `"Professional"` | Expandir com verificação, tipo de atuação (PF/MEI/empresa), documento, anos de experiência, métricas de confiança. |
| `"User"` | Expandir com campos de localização detalhada, telefone obrigatório, provider de auth. |
| `"ProfessionalAvailability"` / `"ProfessionalBlock"` | Manter. Adicionar buffer entre atendimentos como campo configurável no `Professional`. |
| Módulo financeiro (`payment`, `payable`, `ledger_entry`, etc.) | **Manter e estender**. Esse é o módulo correto para pagamentos. Precisa integração com novo `Order` unificado e suporte a sinal/saldo/marcos/recorrência. |
| Cache in-memory + backpressure | Manter. Aplicável aos novos endpoints de listagem. |
| E-mail SMTP | Manter e expandir templates para: proposta enviada, proposta aceita, pagamento confirmado, disputa aberta, conclusão, recontratação. |

### C.2 O que precisa ser criado do zero

| Componente novo | Descrição |
|---|---|
| **Tabela `service_tier`** | Taxonomia de tiers (1-4) com regras: `allowBookingDirect`, `requiresProposal`, `requiresChat`, `allowedPriceFormats`, `defaultSignalPercent`, `maxInstallments`. |
| **Tabela `proposal`** | Proposta formal: `orderId`, `professionalId`, `clientId`, `serviceId`, `scope`, `priceTotal`, `priceByStage` (JSONB), `includesDescription`, `excludesDescription`, `estimatedDuration`, `suggestedDatetime`, `visitFee`, `validUntil`, `status`, `createdAt`. |
| **Tabela `order_timeline`** | Eventos do pedido: `orderId`, `eventType`, `actorId`, `actorRole`, `metadata` (JSONB), `createdAt`. Rastro completo de todo evento. |
| **Tabela `dispute`** | Contestações: `orderId`, `clientId`, `reason`, `evidenceUrls` (JSONB), `professionalResponse`, `resolution`, `resolvedBy`, `status`, `createdAt`, `resolvedAt`. |
| **Tabela `recurring_plan`** | Planos recorrentes: `clientId`, `professionalId`, `serviceId`, `frequency` (weekly/biweekly/monthly), `pricePerSession`, `nextOccurrence`, `status`, `createdAt`. |
| **Tabela `recurring_occurrence`** | Ocorrências individuais do plano: `planId`, `orderId`, `scheduledDate`, `status`. |
| **Tabela `service_category`** | Categorias macro (Encanamento, Elétrica, Limpeza, etc.) vinculadas a `Service`. |
| **Tabela `professional_verification`** | KYC: `professionalId`, `documentType`, `documentNumber`, `selfieUrl`, `documentUrl`, `status`, `verifiedAt`, `rejectionReason`. |
| **Tabela `address`** | Endereço de serviço: `orderId`, `street`, `number`, `complement`, `neighborhood`, `city`, `state`, `zipCode`, `latitude`, `longitude`. |
| **Tabela `message_attachment`** | Anexos de mensagens: `messageId`, `type` (image, file), `url`, `thumbnailUrl`, `fileName`, `sizeBytes`. |
| **Tabela `order_payment`** | Ponte entre novo `Order` unificado e `payment` financeiro: `orderId` (text) → `paymentOrderId` (uuid). Ou: campo `legacy_order_id` na tabela `payment`. |
| **Entidade `Badge`** (futura) | Não bloqueia MVP, mas prever campo `badges` JSONB em `Professional` para extensibilidade. |

### C.3 O que precisa ser removido/refatorado

| Item | Ação |
|---|---|
| Dualidade `"Order"` (PascalCase, text PK) vs `order` (snake_case, UUID PK) | **Unificar progressivamente**. O `"Order"` atual vira o pedido transacional completo. A tabela `order` (minúscula) do módulo financeiro deve referenciar o `"Order"` principal, ou ser migrada. **Decisão recomendada**: manter ambas no curto prazo com bridge table `order_payment`, e unificar em fase posterior. |
| `IConnectionFactory` / `NpgsqlConnectionFactory` (Dapper legado) | **Manter para `PaymentRepository`** (único repositório ainda em Dapper, por causa do módulo financeiro snake_case). Marcar como legacy para migração futura. |
| `PaymentRepository.cs` (Dapper) | **Não migrado para EF Core** — opera em tabelas do esquema financeiro. Será expandido, não removido. |
| Endpoints `GET /orders` e `POST /orders` atuais | **Refatorar contratos**. O `POST /orders` atual cria pedido sem profissional, sem preço, sem pagamento. Deve ser substituído por fluxo novo onde o pedido nasce de booking direto ou aceite de proposta. O endpoint atual pode virar `/orders/legacy` em período de transição. |
| `OrderRules.CanTransition` | **Expandir drasticamente**. Hoje aceita 5 estados. O novo ciclo de vida tem ~14 estados com regras de transição por ator. |
| `CompleteOrderRequest` / `POST /orders/{id}/complete` | **Substituir** por fluxo de conclusão com confirmação do cliente, timeout automático e disputa. |

---

## D. Plano de alterações por módulo (back-end)

### D.1 Taxonomia / Tiers de serviço

**Novas entidades**: `ServiceTier`, `ServiceCategory`

**Novas tabelas**:
- `service_tier` (`id` serial PK, `name` text, `code` text UNIQUE, `allow_booking_direct` bool, `requires_proposal` bool, `requires_chat` bool, `allowed_price_formats` text[], `default_signal_percent` int, `max_installments` int, `cancellation_rules` JSONB)
- `service_category` (`id` text PK, `name` text, `icon` text, `created_at` timestamp)

**Alterações em tabelas existentes**:
- `"Service"`: adicionar `category_id` (FK → `service_category`), `tier_id` (FK → `service_tier`)
- `"ProfessionalService"`: adicionar `tier_id`, `contract_mode` (enum: `booking_direct`, `quote_required`, `both`), `duration_minutes`, `includes_description`, `excludes_description`, `material_included` bool, `visit_fee_cents` int, `min_lead_time_minutes` int

**Novos endpoints**:
- `GET /tiers` — listar tiers com regras
- `GET /categories` — listar categorias
- `GET /services?categoryId=X` — filtrar serviços por categoria
- Admin: `POST /tiers`, `PUT /tiers/{id}`, `POST /categories`

**Arquivos impactados**:
- `src/Domain/Entities/` → novos: `ServiceTier.cs`, `ServiceCategory.cs`; alterados: `Service.cs`, `ProfessionalService.cs`
- `src/Application/Abstractions/` → novo: `IServiceCatalogRepository.cs`
- `src/Application/DTOs/` → novos DTOs de tier e categoria
- `src/Infrastructure/Persistence/Configurations/` → novos: `ServiceTierConfiguration.cs`, `ServiceCategoryConfiguration.cs`; alterados: `ServiceConfiguration.cs`, `ProfessionalServiceConfiguration.cs`
- `src/Infrastructure/Repositories/` → novo: `ServiceCatalogRepository.cs`; alterado: `ProfessionalServiceRepository.cs`
- `src/Infrastructure/Persistence/AppDbContext.cs` → novos DbSets
- `src/Api/Extensions/ApiEndpoints.cs` → novos endpoints

### D.2 Pedido (Order) — objeto central

**Alterações na entity `Order`**:

Campos novos obrigatórios:
- `professionalId` (text, FK → `"Professional"`)
- `tierId` (int, FK → `service_tier`)
- `origin` (enum: `booking_direct`, `proposal_accepted`, `recurring`)
- `proposalId` (text, nullable, FK → `proposal`)
- `appointmentId` (text, nullable, FK → `"Appointment"`)
- `conversationId` (text, nullable, FK → `"Conversation"`)
- `priceTotalCents` (int)
- `signalCents` (int)
- `balanceCents` (int)
- `installments` (int, default 1)
- `paymentMethod` (text, nullable)
- `addressId` (text, nullable, FK → `address`)
- `scope` (text, nullable)
- `scheduledAt` (timestamp, nullable — substitui `date`)
- `completedAt` (timestamp, nullable)
- `cancelledAt` (timestamp, nullable)
- `cancelledBy` (text, nullable)
- `cancellationReason` (text, nullable)
- `autoConfirmAt` (timestamp, nullable — para timeout de 72h)

**Novo enum `OrderStatusV2`** (substituir status textual atual):
```
draft, proposal_sent, awaiting_payment, scheduled, in_transit,
in_progress, awaiting_confirmation, completed, evaluated,
disputed, cancelled_client, cancelled_professional, refunded, rebooked
```

**Regras de transição** (expandir `OrderRules.cs`):
- `draft` → `proposal_sent` (profissional)
- `proposal_sent` → `awaiting_payment` (cliente aceita) | `cancelled_client` | `draft` (negociação)
- `awaiting_payment` → `scheduled` (pagamento sinal OK) | `cancelled_client` (timeout 24h)
- `scheduled` → `in_transit` | `in_progress` | `cancelled_client` | `cancelled_professional`
- `in_progress` → `awaiting_confirmation` (profissional marca concluído)
- `awaiting_confirmation` → `completed` (cliente confirma | auto 72h) | `disputed` (cliente contesta)
- `completed` → `evaluated` | `rebooked`
- `disputed` → `completed` (resolvido) | `refunded`

**Novos endpoints**:
- `POST /orders/booking` — booking direto (Tier 1): cria pedido + cobra sinal
- `POST /orders/from-proposal/{proposalId}` — cria pedido a partir de proposta aceita
- `PUT /orders/{id}/status` — transições de status com validação por ator
- `POST /orders/{id}/confirm-completion` — cliente confirma
- `POST /orders/{id}/dispute` — abre disputa
- `GET /orders/{id}/timeline` — timeline de eventos
- `GET /orders/mine?role=client|professional` — pedidos por papel

**Endpoints alterados**:
- `POST /orders` — deprecar contrato atual; redirecionar para `/orders/booking` ou erro
- `GET /orders` — adicionar filtros por status, tier, profissional
- `POST /orders/{id}/complete` — substituir por fluxo de 2 etapas (pro marca → cliente confirma)

**Arquivos impactados**:
- `src/Domain/Entities/Order.cs` — **reescrever** (record → class com setters para EF tracking de updates)
- `src/Domain/Enums/OrderStatus.cs` — expandir para ~14 estados
- `src/Application/Services/OrderRules.cs` — reescrever com máquina de estados e validação por ator
- `src/Application/Abstractions/IOrderRepository.cs` — expandir interface
- `src/Application/DTOs/Requests.cs` — novos request DTOs
- `src/Infrastructure/Persistence/Configurations/OrderConfiguration.cs` — expandir mapeamento
- `src/Infrastructure/Repositories/OrderRepository.cs` — reescrever
- `src/Api/Extensions/ApiEndpoints.cs` — novos endpoints e refatoração dos existentes

### D.3 Proposta (Proposal)

**Nova entidade**: `Proposal`

**Nova tabela `proposal`**:
- `id` text PK
- `order_id` text nullable (FK → `"Order"`, vinculado após aceite)
- `professional_id` text NOT NULL (FK → `"Professional"`)
- `client_id` text NOT NULL (FK → `"User"`)
- `service_id` text NOT NULL (FK → `"Service"`)
- `professional_service_id` text nullable (FK → `"ProfessionalService"`)
- `conversation_id` text nullable (FK → `"Conversation"`)
- `scope` text NOT NULL
- `includes_description` text
- `excludes_description` text
- `price_total_cents` int NOT NULL
- `price_by_stage` JSONB nullable (para Tier 3: `[{name, amount_cents, order}]`)
- `duration_estimate` text nullable
- `suggested_datetime` timestamp nullable
- `visit_fee_cents` int default 0
- `valid_until` timestamp NOT NULL
- `status` text NOT NULL (enum: `draft`, `sent`, `accepted`, `negotiating`, `rejected`, `expired`)
- `created_at` timestamp NOT NULL
- `updated_at` timestamp NOT NULL

**Novos endpoints**:
- `POST /proposals` — profissional cria proposta
- `GET /proposals/{id}` — detalhes da proposta
- `PUT /proposals/{id}/send` — enviar ao cliente
- `POST /proposals/{id}/accept` — cliente aceita (dispara criação de pedido + cobrança)
- `POST /proposals/{id}/reject` — cliente rejeita (com motivo opcional)
- `POST /proposals/{id}/negotiate` — contraproposta
- `GET /proposals?conversationId=X` — propostas de uma conversa
- `GET /proposals/mine?role=client|professional` — minhas propostas

**Arquivos novos**:
- `src/Domain/Entities/Proposal.cs`
- `src/Application/Abstractions/IProposalRepository.cs`
- `src/Application/DTOs/ProposalDtos.cs`
- `src/Infrastructure/Persistence/Configurations/ProposalConfiguration.cs`
- `src/Infrastructure/Repositories/ProposalRepository.cs`

### D.4 Chat transacional

**Alterações na entity `Message`**:

Campos novos:
- `type` (text, enum: `text`, `proposal`, `schedule_suggestion`, `action`, `system`, `payment`, `completion`, `dispute`)
- `metadata` (JSONB nullable — payload estruturado da ação: ID de proposta, horário sugerido, link de pagamento, etc.)
- `replyToId` (text nullable, FK → `"Message"`)

**Nova tabela `message_attachment`**:
- `id` text PK
- `message_id` text NOT NULL FK → `"Message"`
- `type` text NOT NULL (image, file, photo)
- `url` text NOT NULL
- `thumbnail_url` text nullable
- `file_name` text nullable
- `size_bytes` int nullable
- `created_at` timestamp NOT NULL

**Alterações na entity `Conversation`**:
- Tornar `orderId` mais fortemente vinculado
- Adicionar `status` (text: `active`, `archived`, `flagged`)

**Detecção anti-fuga** (NLP simples):
- Implementar middleware/service que analisa texto de mensagens antes de salvar
- Regex para padrões: telefone (9 dígitos, +55, etc.), e-mail (@), URLs externas
- Se detectado: salvar mensagem normalmente + adicionar `Message` do tipo `system` com aviso educativo
- Não bloquear envio — apenas alertar

**Mascaramento de contato**:
- Até que exista pedido pago vinculado à conversa, telefone e e-mail devem ser ocultados nas respostas de API
- Implementar via projeção nos repositórios: se `order.status < scheduled`, mascarar campos de contato nos DTOs

**Novos endpoints**:
- `POST /messages/attachment` — upload de anexo (multipart)
- `GET /conversations/{id}/actions` — ações transacionais disponíveis no contexto

**Endpoints alterados**:
- `POST /messages` — aceitar `type` e `metadata`
- `GET /messages` — retornar com attachments e metadata

**Arquivos impactados**:
- `src/Domain/Entities/Message.cs` — expandir
- `src/Domain/Entities/Conversation.cs` — expandir
- `src/Infrastructure/Persistence/Configurations/MessageConfiguration.cs` — expandir
- `src/Infrastructure/Repositories/ConversationRepository.cs` — refatorar projeções
- Novo: `src/Application/Services/ContactMaskingService.cs`
- Novo: `src/Application/Services/AntiLeakDetectionService.cs`

### D.5 Pagamento — sinal, saldo, parcelamento, escrow

**Integração com gateway**: o módulo financeiro existente (tabelas `payment`, `payable`, etc.) já opera com Mercado Pago. A expansão requer:

1. **Suporte a sinal (signal) + saldo**: um pedido gera 2 cobranças — sinal no aceite/booking e saldo na conclusão. Campo `payment_type` (enum: `signal`, `balance`, `installment`, `recurring`, `visit_fee`) na tabela `payment`.

2. **Parcelamento**: passar `installments` ao gateway na criação da preferência. Campo já existe conceitualmente no Mercado Pago (`installments` no preference).

3. **Escrow/hold**: dinheiro retido até confirmação. A tabela `payable` com `hold_until` já suporta isso. Expandir lógica para: `hold_until = order.autoConfirmAt`.

4. **Repasse ao profissional**: já existe via `payoutbatch` + `payoutitem`. Precisa ser acionado automaticamente após `order.status = completed`.

5. **Cobrança recorrente**: novo fluxo. Gateway deve suportar `card_token` salvo para cobranças futuras (Mercado Pago suporta via `customer` + `card`).

**Bridge entre esquemas**: o `"Order"` (PascalCase, text PK) precisa ser referenciável pelo módulo financeiro (`order` snake_case, UUID PK). Opções:
- **(Recomendada)**: adicionar coluna `marketplace_order_id` (text) na tabela `order` (minúscula), apontando para `"Order".id`.
- Alternativa: criar tabela bridge `order_payment_link`.

**Novos endpoints**:
- `POST /payments/signal/{orderId}` — cobrar sinal
- `POST /payments/balance/{orderId}` — cobrar saldo (pós-conclusão)
- `POST /payments/visit-fee/{proposalId}` — cobrar taxa de visita
- `GET /payments/order/{orderId}` — todos os pagamentos de um pedido
- `POST /payments/refund/{paymentId}` — reembolso (parcial ou total)

**Arquivos impactados**:
- `src/Infrastructure/Repositories/PaymentRepository.cs` — expandir (continua em Dapper)
- `src/Application/Abstractions/` → expandir `IPaymentRepository` ou novo `IPaymentService`
- Novo: `src/Application/Services/PaymentOrchestrationService.cs` — orquestra sinal → hold → release

### D.6 Disputa e pós-serviço

**Nova entidade**: `Dispute`

**Nova tabela `dispute`**:
- `id` text PK
- `order_id` text NOT NULL FK → `"Order"` UNIQUE
- `client_id` text NOT NULL FK → `"User"`
- `professional_id` text NOT NULL FK → `"Professional"`
- `reason` text NOT NULL
- `description` text
- `evidence_urls` JSONB (array de URLs)
- `professional_response` text nullable
- `professional_evidence_urls` JSONB nullable
- `resolution` text nullable
- `resolved_by` text nullable (system, mediator, agreement)
- `refund_amount_cents` int nullable
- `status` text NOT NULL (opened, professional_responded, mediating, resolved, closed)
- `created_at` timestamp NOT NULL
- `resolved_at` timestamp nullable

**Novos endpoints**:
- `POST /disputes` — abrir disputa (cliente)
- `PUT /disputes/{id}/respond` — profissional responde
- `PUT /disputes/{id}/resolve` — resolução (admin/sistema)
- `GET /disputes/{id}` — detalhes

**Timeline de eventos** (`order_timeline`):
- `id` text PK
- `order_id` text NOT NULL FK → `"Order"`
- `event_type` text NOT NULL
- `actor_id` text nullable
- `actor_role` text nullable (client, professional, system)
- `metadata` JSONB nullable
- `created_at` timestamp NOT NULL

Todo endpoint que altera status do pedido deve inserir evento na timeline.

### D.7 Avaliações expandidas

**Alterações em `Review`**:
- Adicionar: `punctuality_rating` int nullable (1-5), `quality_rating` int nullable, `communication_rating` int nullable, `cleanliness_rating` int nullable
- Adicionar: `photo_urls` JSONB nullable
- Adicionar: `professional_review_of_client` text nullable + `professional_rating_of_client` int nullable
- Adicionar: `client_visible_at` timestamp nullable, `professional_visible_at` timestamp nullable (double-blind)
- Adicionar: `is_verified` bool default false (vinculado a pedido concluído com pagamento)

**Double-blind**: ambas as avaliações ficam invisíveis até que ambos avaliem ou 7 dias passem. `client_visible_at` e `professional_visible_at` são preenchidos pelo sistema.

**Endpoint alterado**: `POST /reviews` — aceitar categorias de nota e fotos.

**Novo endpoint**: `POST /reviews/professional` — profissional avalia cliente.

### D.8 Recorrência (Tier 4)

**Novas entidades**: `RecurringPlan`, `RecurringOccurrence`

**Tabela `recurring_plan`**:
- `id` text PK
- `client_id` text NOT NULL FK → `"User"`
- `professional_id` text NOT NULL FK → `"Professional"`
- `professional_service_id` text NOT NULL FK → `"ProfessionalService"`
- `frequency` text NOT NULL (weekly, biweekly, monthly, custom)
- `frequency_days` int nullable (para custom)
- `price_per_session_cents` int NOT NULL
- `discount_percent` int default 0
- `next_occurrence` timestamp NOT NULL
- `status` text NOT NULL (active, paused, cancelled)
- `created_at` timestamp NOT NULL
- `cancelled_at` timestamp nullable

**Tabela `recurring_occurrence`**:
- `id` text PK
- `plan_id` text NOT NULL FK → `recurring_plan`
- `order_id` text nullable FK → `"Order"`
- `scheduled_date` timestamp NOT NULL
- `status` text NOT NULL (pending, confirmed, skipped, cancelled)

**Background job**: worker que roda diariamente para:
1. Criar pedidos automáticos para ocorrências pendentes
2. Cobrar pagamento recorrente
3. Notificar profissional e cliente

**Novos endpoints**:
- `POST /recurring-plans` — criar plano (após primeiro serviço concluído)
- `GET /recurring-plans/mine` — meus planos
- `PUT /recurring-plans/{id}/pause`
- `PUT /recurring-plans/{id}/resume`
- `DELETE /recurring-plans/{id}` — cancelar
- `GET /recurring-plans/{id}/occurrences`

### D.9 Profissional — expansão de perfil e verificação

**Alterações em `Professional`**:
- Adicionar: `entity_type` text (PF, MEI, empresa)
- Adicionar: `document_number` text nullable (CPF/CNPJ — encriptado)
- Adicionar: `years_of_experience` int nullable
- Adicionar: `specialties` text[] nullable
- Adicionar: `response_rate` float nullable
- Adicionar: `avg_response_time_minutes` int nullable
- Adicionar: `completion_rate` float nullable
- Adicionar: `verification_status` text (pending, verified, rejected)
- Adicionar: `badges` JSONB nullable
- Adicionar: `buffer_minutes` int default 0

**Nova tabela `professional_verification`** (conforme D.2 acima).

**Endpoint alterado**: `POST /professionals` — aceitar campos de onboarding expandido.

**Novo endpoint**: `POST /professionals/{id}/verify` — submit de verificação.

### D.10 Autenticação — expansão

**Estado atual**: login por e-mail + bcrypt. Sem social login, sem OTP.

**Necessário para o fluxo futuro**:
- Social login (Google, Apple) — implementar via Supabase Auth (já existe infra no `.env`)
- OTP por telefone — via Supabase Auth ou Twilio
- Migração do auth manual para Supabase Auth middleware

**Impacto**: o `POST /auth` atual com bcrypt manual precisa coexistir com Supabase JWT durante transição. O middleware de JWT do Supabase (placeholder atual) precisa ser ativado.

**Arquivos impactados**:
- `src/Api/Program.cs` — ativar middleware JWT
- `src/Infrastructure/Repositories/AuthRepository.cs` — manter como fallback
- Novo: `src/Api/Middleware/SupabaseAuthMiddleware.cs`

### D.11 Busca e listagem — expansão

**Alterações em `GET /professionals`**:
- Filtrar por: tier, categoria, modo de contratação, preço (faixa), nota mínima, verificado, distância
- Ordenar por: relevância (combinação de nota, proximidade, disponibilidade), preço, nota

**Alterações em `GET /bootstrap`**:
- Incluir `categories` e `tiers` na resposta agregada

**Novo endpoint**: `GET /search` — busca unificada por texto com match de serviço + profissional.

### D.12 Background jobs / Workers

Atualmente **não existe nenhum worker de background**. O PRD exige:

| Job | Trigger | Ação |
|---|---|---|
| `ProposalExpirationJob` | A cada hora | Expirar propostas com `valid_until < now()` e `status = sent` |
| `PaymentTimeoutJob` | A cada hora | Cancelar pedidos com `status = awaiting_payment` e criados há >24h |
| `AutoConfirmationJob` | A cada hora | Auto-confirmar pedidos com `status = awaiting_confirmation` e `autoConfirmAt < now()` → liberar pagamento |
| `RecurringBillingJob` | Diário | Criar pedidos e cobrar para planos recorrentes com `next_occurrence <= today` |
| `ConversationNudgeJob` | Diário | Enviar notificação para conversas sem proposta após 7 dias |

**Implementação recomendada**: `IHostedService` + `PeriodicTimer` no `Api` project (roda junto da Lambda via warm instances ou separado em ECS task).

---

## E. Mapa de arquivos impactados

### Arquivos existentes a alterar

| Arquivo | Tipo de alteração |
|---|---|
| `src/Domain/Entities/Order.cs` | Reescrever — expandir de 8 para ~25 campos |
| `src/Domain/Entities/Professional.cs` | Expandir com ~10 campos |
| `src/Domain/Entities/Message.cs` | Expandir com `type`, `metadata`, `replyToId` |
| `src/Domain/Entities/Conversation.cs` | Expandir com `status` |
| `src/Domain/Entities/ProfessionalService.cs` | Expandir com tier, contrato, duração, etc. |
| `src/Domain/Entities/Review.cs` | Expandir com categorias, fotos, double-blind |
| `src/Domain/Entities/Service.cs` | Adicionar `categoryId`, `tierId` |
| `src/Domain/Enums/OrderStatus.cs` | Reescrever com ~14 estados |
| `src/Application/Services/OrderRules.cs` | Reescrever — máquina de estados completa |
| `src/Application/Abstractions/IOrderRepository.cs` | Expandir interface |
| `src/Application/Abstractions/IProfessionalReadRepository.cs` | Expandir filtros |
| `src/Application/DTOs/Requests.cs` | Adicionar ~15 novos request DTOs |
| `src/Application/DTOs/ProfessionalCardDto.cs` | Adicionar tier, modo contratação, badge |
| `src/Application/DTOs/HomeDtos.cs` | Adicionar categories, tiers |
| `src/Infrastructure/Persistence/AppDbContext.cs` | Adicionar ~10 novos DbSets |
| `src/Infrastructure/Persistence/Configurations/OrderConfiguration.cs` | Reescrever |
| `src/Infrastructure/Persistence/Configurations/ProfessionalConfiguration.cs` | Expandir |
| `src/Infrastructure/Persistence/Configurations/ProfessionalServiceConfiguration.cs` | Expandir |
| `src/Infrastructure/Persistence/Configurations/MessageConfiguration.cs` | Expandir |
| `src/Infrastructure/Persistence/Configurations/ReviewConfiguration.cs` | Expandir |
| `src/Infrastructure/Persistence/Configurations/ServiceConfiguration.cs` | Expandir |
| `src/Infrastructure/Repositories/OrderRepository.cs` | Reescrever |
| `src/Infrastructure/Repositories/ConversationRepository.cs` | Refatorar projeções |
| `src/Infrastructure/Repositories/ProfessionalReadRepository.cs` | Expandir filtros |
| `src/Infrastructure/Repositories/ReviewRepository.cs` | Expandir |
| `src/Infrastructure/Repositories/PaymentRepository.cs` | Expandir (Dapper) |
| `src/Infrastructure/ServiceCollectionExtensions.cs` | Registrar novos serviços/repos |
| `src/Infrastructure/Email/EmailTemplates.cs` | Adicionar ~8 templates |
| `src/Api/Extensions/ApiEndpoints.cs` | Adicionar ~25 endpoints, refatorar ~10 |
| `src/Api/Program.cs` | Ativar auth JWT, registrar hosted services |

### Arquivos novos a criar

| Arquivo | Propósito |
|---|---|
| `src/Domain/Entities/Proposal.cs` | Entidade proposta |
| `src/Domain/Entities/Dispute.cs` | Entidade disputa |
| `src/Domain/Entities/OrderTimeline.cs` | Entidade timeline |
| `src/Domain/Entities/RecurringPlan.cs` | Plano recorrente |
| `src/Domain/Entities/RecurringOccurrence.cs` | Ocorrência recorrente |
| `src/Domain/Entities/ServiceTier.cs` | Tier de serviço |
| `src/Domain/Entities/ServiceCategory.cs` | Categoria de serviço |
| `src/Domain/Entities/ProfessionalVerification.cs` | Verificação KYC |
| `src/Domain/Entities/Address.cs` | Endereço de serviço |
| `src/Domain/Entities/MessageAttachment.cs` | Anexo de mensagem |
| `src/Domain/Enums/ProposalStatus.cs` | Enum de status de proposta |
| `src/Domain/Enums/DisputeStatus.cs` | Enum de status de disputa |
| `src/Domain/Enums/ContractMode.cs` | Enum de modo de contratação |
| `src/Domain/Enums/MessageType.cs` | Enum de tipo de mensagem |
| `src/Domain/Enums/PriceFormat.cs` | Enum de formato de preço |
| `src/Application/Abstractions/IProposalRepository.cs` | Interface proposta |
| `src/Application/Abstractions/IDisputeRepository.cs` | Interface disputa |
| `src/Application/Abstractions/IRecurringPlanRepository.cs` | Interface recorrência |
| `src/Application/Abstractions/IAddressRepository.cs` | Interface endereço |
| `src/Application/Services/PaymentOrchestrationService.cs` | Orquestração sinal/saldo |
| `src/Application/Services/ContactMaskingService.cs` | Mascaramento de contato |
| `src/Application/Services/AntiLeakDetectionService.cs` | Detecção anti-fuga |
| `src/Application/Services/OrderStateMachine.cs` | Máquina de estados do pedido |
| `src/Application/Services/TimeoutService.cs` | Lógica de timeouts |
| `src/Infrastructure/Persistence/Configurations/ProposalConfiguration.cs` | Mapeamento EF |
| `src/Infrastructure/Persistence/Configurations/DisputeConfiguration.cs` | Mapeamento EF |
| `src/Infrastructure/Persistence/Configurations/OrderTimelineConfiguration.cs` | Mapeamento EF |
| `src/Infrastructure/Persistence/Configurations/RecurringPlanConfiguration.cs` | Mapeamento EF |
| `src/Infrastructure/Persistence/Configurations/AddressConfiguration.cs` | Mapeamento EF |
| `src/Infrastructure/Persistence/Configurations/MessageAttachmentConfiguration.cs` | Mapeamento EF |
| `src/Infrastructure/Persistence/Configurations/ServiceCategoryConfiguration.cs` | Mapeamento EF |
| `src/Infrastructure/Persistence/Configurations/ServiceTierConfiguration.cs` | Mapeamento EF |
| `src/Infrastructure/Repositories/ProposalRepository.cs` | Repositório |
| `src/Infrastructure/Repositories/DisputeRepository.cs` | Repositório |
| `src/Infrastructure/Repositories/RecurringPlanRepository.cs` | Repositório |
| `src/Infrastructure/Repositories/AddressRepository.cs` | Repositório |
| `src/Infrastructure/BackgroundJobs/ProposalExpirationJob.cs` | Hosted service |
| `src/Infrastructure/BackgroundJobs/AutoConfirmationJob.cs` | Hosted service |
| `src/Infrastructure/BackgroundJobs/PaymentTimeoutJob.cs` | Hosted service |
| `src/Infrastructure/BackgroundJobs/RecurringBillingJob.cs` | Hosted service |
| `src/Api/Middleware/SupabaseAuthMiddleware.cs` | Auth JWT |

---

## F. Mapa de banco de dados

### F.1 Tabelas novas

| Tabela | PK | Estimativa de colunas | Índices principais |
|---|---|---|---|
| `service_tier` | `id` serial | ~10 | `code` UNIQUE |
| `service_category` | `id` text | ~4 | `name` |
| `proposal` | `id` text | ~18 | `order_id`, `professional_id`, `client_id`, `conversation_id`, `status`, `valid_until` |
| `order_timeline` | `id` text | ~6 | `order_id + created_at`, `event_type` |
| `dispute` | `id` text | ~14 | `order_id` UNIQUE, `status` |
| `recurring_plan` | `id` text | ~12 | `client_id`, `professional_id`, `status`, `next_occurrence` |
| `recurring_occurrence` | `id` text | ~5 | `plan_id`, `scheduled_date`, `status` |
| `professional_verification` | `id` text | ~10 | `professional_id` UNIQUE, `status` |
| `address` | `id` text | ~12 | `order_id` |
| `message_attachment` | `id` text | ~7 | `message_id` |

### F.2 Colunas novas em tabelas existentes

| Tabela | Colunas novas |
|---|---|
| `"Order"` | `"professionalId"`, `"tierId"`, `"origin"`, `"proposalId"`, `"appointmentId"`, `"conversationId"`, `"priceTotalCents"`, `"signalCents"`, `"balanceCents"`, `"installments"`, `"paymentMethod"`, `"addressId"`, `"scope"`, `"scheduledAt"`, `"completedAt"`, `"cancelledAt"`, `"cancelledBy"`, `"cancellationReason"`, `"autoConfirmAt"` |
| `"Service"` | `"categoryId"`, `"tierId"` |
| `"ProfessionalService"` | `"tierId"`, `"contractMode"`, `"durationMinutes"`, `"includesDescription"`, `"excludesDescription"`, `"materialIncluded"`, `"visitFeeCents"`, `"minLeadTimeMinutes"` |
| `"Professional"` | `"entityType"`, `"documentNumber"`, `"yearsOfExperience"`, `"specialties"`, `"responseRate"`, `"avgResponseTimeMinutes"`, `"completionRate"`, `"verificationStatus"`, `"badges"`, `"bufferMinutes"` |
| `"Message"` | `"type"`, `"metadata"`, `"replyToId"` |
| `"Conversation"` | `"status"` |
| `"Review"` | `"punctualityRating"`, `"qualityRating"`, `"communicationRating"`, `"cleanlinessRating"`, `"photoUrls"`, `"professionalReviewOfClient"`, `"professionalRatingOfClient"`, `"clientVisibleAt"`, `"professionalVisibleAt"`, `"isVerified"` |
| `order` (minúscula, financeiro) | `marketplace_order_id` text nullable |

### F.3 Enums novos (PG ou aplicação)

| Enum | Valores |
|---|---|
| `order_status_v2` | draft, proposal_sent, awaiting_payment, scheduled, in_transit, in_progress, awaiting_confirmation, completed, evaluated, disputed, cancelled_client, cancelled_professional, refunded, rebooked |
| `contract_mode` | booking_direct, quote_required, both |
| `proposal_status` | draft, sent, accepted, negotiating, rejected, expired |
| `dispute_status` | opened, professional_responded, mediating, resolved, closed |
| `message_type` | text, proposal, schedule_suggestion, action, system, payment, completion, dispute |
| `price_format` | fixed, hourly, starting_at, quote, recurring |
| `entity_type` | pf, mei, empresa |
| `verification_status` | pending, in_review, verified, rejected |
| `payment_type` | signal, balance, installment, recurring, visit_fee |
| `recurring_frequency` | weekly, biweekly, monthly, custom |

### F.4 Migrações estimadas

1. `AddServiceTiersAndCategories` — tabelas de taxonomia + FK em Service e ProfessionalService
2. `ExpandOrderForTransactionalModel` — colunas novas em Order + enum
3. `AddProposalTable` — proposta completa
4. `AddOrderTimelineAndDispute` — timeline + disputa
5. `ExpandChatForTransactional` — type/metadata em Message + attachments + status em Conversation
6. `ExpandProfessionalVerification` — verification table + campos em Professional
7. `ExpandReviewDoubleBlind` — campos novos em Review
8. `AddRecurringPlan` — recurring_plan + recurring_occurrence
9. `AddAddressTable` — endereço de serviço
10. `BridgeFinancialModule` — marketplace_order_id em order (minúscula)

---

## G. Ordem ideal de implementação (fases)

### Fase 0 — Fundação (pré-requisito, ~1 semana)
- [ ] Seed de `service_tier` (4 tiers) e `service_category` (~15 categorias iniciais)
- [ ] Migração: FK `tierId` em `Service` e `ProfessionalService`
- [ ] Expandir entity `Professional` com campos de verificação e métricas (nullable, sem obrigatoriedade)
- [ ] Ativar middleware JWT do Supabase (coexistir com auth manual)
- [ ] Migração: `marketplace_order_id` em tabela `order` (financeiro)

### Fase 1 — Pedido + Proposta + Pagamento MVP (~3-4 semanas)
- [ ] Reescrever entity `Order` com todos os campos novos (colunas nullable para retrocompatibilidade)
- [ ] Implementar `OrderStateMachine` com validação por ator
- [ ] Implementar `Proposal` (entity, table, repo, endpoints)
- [ ] Implementar `OrderTimeline` (entity, table, repo)
- [ ] Endpoint `POST /orders/booking` (Tier 1 — booking direto)
- [ ] Endpoint `POST /orders/from-proposal/{id}` (Tier 2/3 — via proposta)
- [ ] Integrar cobrança de sinal com módulo financeiro existente
- [ ] Fluxo de conclusão: profissional marca → cliente confirma → pagamento liberado
- [ ] Background job: `PaymentTimeoutJob` + `AutoConfirmationJob`
- [ ] Testes de integração para fluxo completo booking → conclusão

**Dependência**: Fase 0 concluída.

### Fase 2 — Chat transacional + anti-fuga (~2 semanas)
- [ ] Expandir `Message` com `type`, `metadata`
- [ ] Implementar `message_attachment` (upload + storage)
- [ ] Ações transacionais no chat: enviar proposta, aceitar, sugerir horário
- [ ] Detecção anti-fuga (regex + mensagem system)
- [ ] Mascaramento de contato (projeção condicional nos DTOs)
- [ ] Expandir `Conversation` com status

**Dependência**: Fase 1 (proposta precisa existir para ação no chat).

### Fase 3 — Disputa + avaliação expandida (~2 semanas)
- [ ] Implementar `Dispute` (entity, table, repo, endpoints)
- [ ] Integrar disputa no ciclo de vida do pedido
- [ ] Expandir `Review` com categorias, fotos, double-blind
- [ ] Background job: `ProposalExpirationJob`
- [ ] Templates de e-mail para todos os eventos transacionais

**Dependência**: Fase 1 (pedido com status `awaiting_confirmation` precisa existir).

### Fase 4 — Recorrência + recontratação (~2 semanas)
- [ ] Implementar `RecurringPlan` + `RecurringOccurrence`
- [ ] Background job: `RecurringBillingJob`
- [ ] Endpoint de recontratação (`POST /orders/rebook/{orderId}`)
- [ ] Lógica de desconto recorrente

**Dependência**: Fase 1 (precisa de pedido concluído como base).

### Fase 5 — Verificação e métricas de confiança (~1-2 semanas)
- [ ] Implementar `ProfessionalVerification` (upload, status)
- [ ] Cálculo de métricas: response_rate, avg_response_time, completion_rate
- [ ] Badges automáticos (verificado, top pro)
- [ ] Atualizar `GET /professionals` com filtros de verificação e nota

**Dependência**: pode rodar em paralelo com Fase 3/4.

---

## H. Riscos técnicos, dependências e trade-offs

### Riscos técnicos

| Risco | Severidade | Mitigação |
|---|---|---|
| **Dualidade de esquemas** (PascalCase vs snake_case) no banco | Alta | Bridge via `marketplace_order_id`. Não tentar migração big-bang dos dados financeiros. |
| **Entidades como `record` imutáveis** no Domain | Média | Records impedem tracking de updates no EF Core. Converter `Order` e `Professional` para classes com setters privados para permitir `ExecuteUpdateAsync`. Alternativa: manter records e usar apenas `ExecuteUpdateAsync` (sem change tracker). |
| **Endpoint monolítico `ApiEndpoints.cs`** (~500 linhas) | Média | Quebrar em arquivos por domínio: `OrderEndpoints.cs`, `ProposalEndpoints.cs`, `ChatEndpoints.cs`, etc. usando extension methods. |
| **Lambda cold start** com EF Core + background jobs | Alta | Background jobs em Lambda são inviáveis com `IHostedService`. Alternativa: usar EventBridge Scheduler + Lambda functions separadas, ou migrar para ECS para workers. |
| **Retrocompatibilidade de API** com front-end existente | Alta | Manter endpoints atuais funcionando durante transição. Novos endpoints em paralelo. Usar versionamento implícito via novos paths (`/v2/orders` ou caminhos distintos como `/orders/booking`). |
| **Timeout de 72h para auto-confirmação** sem cron | Alta | Em Lambda, usar EventBridge Scheduled Rules apontando para endpoint interno. Ou DynamoDB TTL + stream → Lambda. |

### Dependências externas

| Dependência | Status | Impacto se ausente |
|---|---|---|
| Gateway de pagamento (Mercado Pago) completo | Parcialmente implementado | Bloqueia Fase 1 — sem sinal/saldo não há pedido protegido |
| Supabase Auth (social login, OTP) | Infra existe, middleware é placeholder | Bloqueia experiência de cadastro expandido, mas não bloqueia fluxo core |
| Storage de arquivos (Supabase Storage) | Implementado para avatares | Precisa extensão para anexos de chat e evidências de disputa |
| Push notifications (FCM/APNs) | Não existe | Reduz engajamento significativamente — necessário para timeouts e ações transacionais |
| NLP anti-fuga | Não existe | Regex cobre 80% dos casos; NLP é melhoria futura |

### Trade-offs documentados

| Decisão | Trade-off |
|---|---|
| Manter Dapper no módulo financeiro | Evita migração de risco do esquema snake_case, mas mantém duas tecnologias de acesso a dados. |
| Records imutáveis no Domain | Padrão DDD puro, mas incompatível com change tracking do EF Core. Aceitar uso exclusivo de `ExecuteUpdateAsync` para mutations. |
| Criar pedido via `POST /orders/booking` separado | Quebra compatibilidade do `POST /orders` atual, mas o contrato atual é insuficiente. Manter o antigo como deprecated. |
| Background jobs via Lambda + EventBridge (não IHostedService) | Mais complexo de configurar, mas funciona em ambiente serverless. Alternativa: migrar API para ECS e usar IHostedService normalmente. |

---

## I. Checklist final para o agente de código

Antes de começar a implementar, o agente de código deve confirmar:

- [ ] **Fase 0**: seed de tiers e categorias está aplicado no banco
- [ ] **Entity Order**: decisão entre manter `record` (com `ExecuteUpdateAsync` apenas) ou migrar para `class`
- [ ] **Background jobs**: decisão entre IHostedService (requer ECS) ou EventBridge + Lambda separada
- [ ] **Auth**: decisão sobre ativar Supabase JWT agora ou manter bcrypt manual
- [ ] **Módulo financeiro**: confirmação de que `marketplace_order_id` é suficiente como bridge
- [ ] **Endpoints legados**: confirmação de que front-end atual pode conviver com novos endpoints sem quebrar
- [ ] **Push notifications**: decisão sobre incluir na Fase 1 ou postergar

Cada fase deve ser entregue como PR atômica com:
- migração EF Core
- script SQL idempotente
- entidades + configurações + repositórios
- endpoints
- testes de integração cobrindo fluxo completo
- documentação de contrato de API atualizada
