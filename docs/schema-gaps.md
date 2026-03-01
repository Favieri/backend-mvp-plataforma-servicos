# Schema Gaps

Referências SQL encontradas no código que não constam em `docs/db-schema-dump.json`.

| arquivo | método | item | decisão |
|---|---|---|---|
| `src/Infrastructure/Repositories/PaymentRepository.cs` | `TryStartWebhookProcessingAsync/MarkWebhookProcessedAsync` | `table:webhook_events` | `schema_gap`: mantido sem alteração por não existir no dump e não haver substituição inequívoca. |
| `src/Infrastructure/Repositories/PaymentRepository.cs` | `TryStartWebhookProcessingAsync/MarkWebhookProcessedAsync` | `column:webhook_events.provider` | `schema_gap`: mantido sem alteração por não existir no dump e não haver substituição inequívoca. |
| `src/Infrastructure/Repositories/PaymentRepository.cs` | `TryStartWebhookProcessingAsync/MarkWebhookProcessedAsync` | `column:webhook_events.event_id` | `schema_gap`: mantido sem alteração por não existir no dump e não haver substituição inequívoca. |
| `src/Infrastructure/Repositories/PaymentRepository.cs` | `TryStartWebhookProcessingAsync/MarkWebhookProcessedAsync` | `column:webhook_events.raw_payload` | `schema_gap`: mantido sem alteração por não existir no dump e não haver substituição inequívoca. |
| `src/Infrastructure/Repositories/PaymentRepository.cs` | `TryStartWebhookProcessingAsync/MarkWebhookProcessedAsync` | `column:webhook_events.status` | `schema_gap`: mantido sem alteração por não existir no dump e não haver substituição inequívoca. |
| `src/Infrastructure/Repositories/PaymentRepository.cs` | `TryStartWebhookProcessingAsync/MarkWebhookProcessedAsync` | `column:webhook_events.created_at` | `schema_gap`: mantido sem alteração por não existir no dump e não haver substituição inequívoca. |
| `src/Infrastructure/Repositories/PaymentRepository.cs` | `TryStartWebhookProcessingAsync/MarkWebhookProcessedAsync` | `column:webhook_events.processed_at` | `schema_gap`: mantido sem alteração por não existir no dump e não haver substituição inequívoca. |