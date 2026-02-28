# DB Schema Map

Total de tabelas: **32**.

## Conflitos de case em tabelas
- `order`: `Order`, `order`
- `professional`: `Professional`, `professional`

## Tabelas e colunas
### `Appointment` ⚠️ tabela com maiúsculas (usar aspas duplas)
- `clientId` **(camelCase/PascalCase: usar aspas duplas)**
- `createdAt` **(camelCase/PascalCase: usar aspas duplas)**
- `endsAt` **(camelCase/PascalCase: usar aspas duplas)**
- `id`
- `location`
- `notes`
- `professionalId` **(camelCase/PascalCase: usar aspas duplas)**
- `serviceId` **(camelCase/PascalCase: usar aspas duplas)**
- `startsAt` **(camelCase/PascalCase: usar aspas duplas)**
- `status`
- `updatedAt` **(camelCase/PascalCase: usar aspas duplas)**

### `Conversation` ⚠️ tabela com maiúsculas (usar aspas duplas)
- `clientId` **(camelCase/PascalCase: usar aspas duplas)**
- `clientLastReadAt` **(camelCase/PascalCase: usar aspas duplas)**
- `createdAt` **(camelCase/PascalCase: usar aspas duplas)**
- `id`
- `orderId` **(camelCase/PascalCase: usar aspas duplas)**
- `professionalId` **(camelCase/PascalCase: usar aspas duplas)**
- `professionalLastReadAt` **(camelCase/PascalCase: usar aspas duplas)**

### `EmailJob` ⚠️ tabela com maiúsculas (usar aspas duplas)
- `attempts`
- `createdAt` **(camelCase/PascalCase: usar aspas duplas)**
- `dedupeKey` **(camelCase/PascalCase: usar aspas duplas)**
- `error`
- `from`
- `html`
- `id`
- `replyTo` **(camelCase/PascalCase: usar aspas duplas)**
- `sentAt` **(camelCase/PascalCase: usar aspas duplas)**
- `status`
- `subject`
- `text`
- `to`

### `ledger_entry`
- `amount_cents`
- `created_at`
- `id`
- `order_id`
- `payment_id`
- `payout_item_id`
- `professional_id`
- `type`

### `Message` ⚠️ tabela com maiúsculas (usar aspas duplas)
- `conversationId` **(camelCase/PascalCase: usar aspas duplas)**
- `id`
- `senderId` **(camelCase/PascalCase: usar aspas duplas)**
- `sentAt` **(camelCase/PascalCase: usar aspas duplas)**
- `text`

### `Order` ⚠️ tabela com maiúsculas (usar aspas duplas)
- `clientId` **(camelCase/PascalCase: usar aspas duplas)**
- `createdAt` **(camelCase/PascalCase: usar aspas duplas)**
- `date`
- `description`
- `id`
- `location`
- `serviceId` **(camelCase/PascalCase: usar aspas duplas)**
- `status`

### `order`
- `amount_cents`
- `completed_at`
- `created_at`
- `customer_id`
- `id`
- `professional_id`
- `status`
- `updated_at`

### `payable`
- `amount_cents`
- `created_at`
- `hold_until`
- `id`
- `order_id`
- `payout_item_id`
- `professional_id`
- `status`

### `payment`
- `amount_cents`
- `created_at`
- `gateway`
- `gateway_fee_cents`
- `gateway_ref`
- `id`
- `method`
- `order_id`
- `paid_at`
- `platform_fee_cents`
- `status`
- `updated_at`

### `payoutbatch`
- `created_at`
- `created_by`
- `file_url`
- `id`
- `period_from`
- `period_to`
- `status`

### `payoutitem`
- `amount_cents`
- `batch_id`
- `created_at`
- `destination_snap`
- `error_message`
- `id`
- `professional_id`
- `status`
- `transfer_ref`

### `Professional` ⚠️ tabela com maiúsculas (usar aspas duplas)
- `active`
- `allowInstantBooking` **(camelCase/PascalCase: usar aspas duplas)**
- `availabilityText` **(camelCase/PascalCase: usar aspas duplas)**
- `avatarUrl` **(camelCase/PascalCase: usar aspas duplas)**
- `bio`
- `completedJobsCount` **(camelCase/PascalCase: usar aspas duplas)**
- `id`
- `leadTimeMinutes` **(camelCase/PascalCase: usar aspas duplas)**
- `maxAdvanceDays` **(camelCase/PascalCase: usar aspas duplas)**
- `rating`
- `slotMinutes` **(camelCase/PascalCase: usar aspas duplas)**
- `userId` **(camelCase/PascalCase: usar aspas duplas)**

### `professional`
- `created_at`
- `display_name`
- `id`
- `payout_method_id`
- `updated_at`
- `user_id`

### `professional_payout_method`
- `account`
- `account_digit`
- `account_type`
- `bank_code`
- `branch`
- `created_at`
- `doc_number`
- `doc_type`
- `holder_name`
- `id`
- `method`
- `pix_key`
- `pix_key_type`
- `professional_id`
- `verified`

### `ProfessionalAvailability` ⚠️ tabela com maiúsculas (usar aspas duplas)
- `active`
- `endMinutes` **(camelCase/PascalCase: usar aspas duplas)**
- `id`
- `professionalId` **(camelCase/PascalCase: usar aspas duplas)**
- `startMinutes` **(camelCase/PascalCase: usar aspas duplas)**
- `weekday`

### `ProfessionalBlock` ⚠️ tabela com maiúsculas (usar aspas duplas)
- `createdAt` **(camelCase/PascalCase: usar aspas duplas)**
- `endsAt` **(camelCase/PascalCase: usar aspas duplas)**
- `id`
- `professionalId` **(camelCase/PascalCase: usar aspas duplas)**
- `reason`
- `startsAt` **(camelCase/PascalCase: usar aspas duplas)**

### `ProfessionalOrderIgnore` ⚠️ tabela com maiúsculas (usar aspas duplas)
- `createdAt` **(camelCase/PascalCase: usar aspas duplas)**
- `orderId` **(camelCase/PascalCase: usar aspas duplas)**
- `professionalId` **(camelCase/PascalCase: usar aspas duplas)**

### `ProfessionalPortfolio` ⚠️ tabela com maiúsculas (usar aspas duplas)
- `createdAt` **(camelCase/PascalCase: usar aspas duplas)**
- `description`
- `id`
- `imageUrl` **(camelCase/PascalCase: usar aspas duplas)**
- `orderIndex` **(camelCase/PascalCase: usar aspas duplas)**
- `professionalId` **(camelCase/PascalCase: usar aspas duplas)**
- `title`

### `ProfessionalService` ⚠️ tabela com maiúsculas (usar aspas duplas)
- `descricao`
- `id`
- `nomeServico` **(camelCase/PascalCase: usar aspas duplas)**
- `preco`
- `professionalId` **(camelCase/PascalCase: usar aspas duplas)**
- `serviceId` **(camelCase/PascalCase: usar aspas duplas)**

### `ProfessionalZone` ⚠️ tabela com maiúsculas (usar aspas duplas)
- `createdAt` **(camelCase/PascalCase: usar aspas duplas)**
- `professionalId` **(camelCase/PascalCase: usar aspas duplas)**
- `zoneId` **(camelCase/PascalCase: usar aspas duplas)**

### `Review` ⚠️ tabela com maiúsculas (usar aspas duplas)
- `clientId` **(camelCase/PascalCase: usar aspas duplas)**
- `comment`
- `createdAt` **(camelCase/PascalCase: usar aspas duplas)**
- `id`
- `orderId` **(camelCase/PascalCase: usar aspas duplas)**
- `professionalId` **(camelCase/PascalCase: usar aspas duplas)**
- `rating`

### `Service` ⚠️ tabela com maiúsculas (usar aspas duplas)
- `createdAt` **(camelCase/PascalCase: usar aspas duplas)**
- `icon`
- `id`
- `name`

### `User` ⚠️ tabela com maiúsculas (usar aspas duplas)
- `createdAt` **(camelCase/PascalCase: usar aspas duplas)**
- `email`
- `id`
- `name`
- `phone`
- `role`
- `senha`
- `zoneId` **(camelCase/PascalCase: usar aspas duplas)**

### `v_gmv_daily`
- `day`
- `gmv`

### `v_gmv_daily_plot`
- `gmv`
- `net`
- `payments_count`
- `platform_revenue`
- `ts`

### `v_gmv_hourly_plot`
- `gmv`
- `net`
- `payments_count`
- `platform_revenue`
- `ts`

### `v_orders_payments_daily`
- `conversion_rate`
- `failed_rate`
- `jobs_completed`
- `orders_created`
- `payments_failed`
- `payments_paid`
- `ts`

### `v_orders_to_paid_30d`
- `pedidos_pagos`
- `pedidos_sem_pagamento`
- `taxa_conversao`

### `v_payables_status`
- `qtd`
- `status`
- `total`

### `v_payouts_7d`
- `pros`
- `status`
- `total`

### `v_revenue_cost_daily`
- `day`
- `doezy_revenue`
- `gateway_cost`
- `gross_margin`

### `Zone` ⚠️ tabela com maiúsculas (usar aspas duplas)
- `active`
- `createdAt` **(camelCase/PascalCase: usar aspas duplas)**
- `id`
- `name`
