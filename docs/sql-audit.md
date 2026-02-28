# SQL Audit

| arquivo | método | tabelas referenciadas | colunas referenciadas | status |
|---|---|---|---|---|
| `src/Infrastructure/Repositories/OrderRepository.cs` | `GetOrdersAsync` | `"Order"`, `"User"`, `"ProfessionalZone"`, `"ProfessionalOrderIgnore"` | `id`, `"clientId"`, `"serviceId"`, `description`, `location`, `date`, `status`, `"createdAt"`, `"zoneId"`, `"professionalId"`, `"orderId"` | `corrigido` |
| `src/Infrastructure/Repositories/OrderRepository.cs` | `GetByIdAsync` | `"Order"` | `id`, `"clientId"`, `"serviceId"`, `description`, `location`, `date`, `status`, `"createdAt"` | `corrigido` |
| `src/Infrastructure/Repositories/OrderRepository.cs` | `CreateAsync` | `"Order"` | `id`, `"clientId"`, `"serviceId"`, `description`, `location`, `date`, `status`, `"createdAt"` | `corrigido` |
| `src/Infrastructure/Repositories/OrderRepository.cs` | `CompleteOrderAsync` | `"Order"` | `id`, `status` | `ok` |
| `src/Infrastructure/Repositories/OrderRepository.cs` | `GetMineAsync` | `"Order"` | `id`, `status`, `date`, `"createdAt"`, `"clientId"` | `corrigido` |
| `src/Infrastructure/Repositories/AppointmentRepository.cs` | `GetByClientAsync` | `"Appointment"` | `id`, `"professionalId"`, `"clientId"`, `"serviceId"`, `"startsAt"`, `"endsAt"`, `status`, `location`, `notes` | `corrigido` |
| `src/Infrastructure/Repositories/AppointmentRepository.cs` | `CreateAsync` | `"Appointment"` | `id`, `"professionalId"`, `"clientId"`, `"serviceId"`, `"startsAt"`, `"endsAt"`, `status`, `location`, `notes`, `"createdAt"`, `"updatedAt"` | `corrigido` |
| `src/Infrastructure/Repositories/AppointmentRepository.cs` | `UpdateStatusAsync` | `"Appointment"` | `id`, `status`, `"updatedAt"`, `"professionalId"`, `"clientId"`, `"serviceId"`, `"startsAt"`, `"endsAt"`, `location`, `notes` | `corrigido` |
| `src/Infrastructure/Repositories/AuthRepository.cs` | `LoginAsync` | `"User"` | `id`, `name`, `email`, `phone`, `role`, `senha`, `"createdAt"` | `corrigido` |
| `src/Infrastructure/Repositories/PaymentRepository.cs` | `UpsertAsync` | `payment` | `id`, `order_id`, `gateway`, `gateway_ref`, `method`, `amount_cents`, `status`, `created_at`, `updated_at`, `paid_at` | `ok` |
| `src/Infrastructure/Repositories/PaymentRepository.cs` | `GetLatestByOrderAsync` | `payment` | `id`, `order_id`, `gateway`, `gateway_ref`, `method`, `amount_cents`, `status`, `created_at`, `paid_at` | `ok` |
| `src/Infrastructure/Repositories/PaymentRepository.cs` | `TryStartWebhookProcessingAsync` | `webhook_events` | `provider`, `event_id`, `raw_payload`, `status`, `created_at` | `gap` |
| `src/Infrastructure/Repositories/PaymentRepository.cs` | `MarkWebhookProcessedAsync` | `webhook_events` | `status`, `processed_at`, `provider`, `event_id` | `gap` |
| `src/Infrastructure/Repositories/PaymentRepository.cs` | `ApplyPaymentStatusAsync` | `payment`, `"Order"` | `status`, `paid_at`, `updated_at`, `gateway_ref`, `id`, `order_id` | `ok` |
| `src/Infrastructure/Repositories/PaymentRepository.cs` | `GetWalletBalanceAsync` | `ledger_entry` | `amount_cents`, `professional_id` | `ok` |
| `src/Infrastructure/Repositories/PaymentRepository.cs` | `GetLedgerAsync` | `ledger_entry` | `id`, `type`, `amount_cents`, `created_at`, `order_id`, `payment_id`, `payout_item_id`, `professional_id` | `ok` |
