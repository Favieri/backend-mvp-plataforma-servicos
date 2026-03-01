# API Contracts

## GET /professionals

Retorna cards completos de profissionais para listagem do frontend.

### Query params suportados

- `serviceId` (opcional): filtra profissionais que oferecem o serviço.
- `zoneId` (opcional): filtra profissionais por zona específica.
- `excludeProfessionalId` (opcional): exclui um profissional da lista.
- `professionalId` + `filterZones=true` (opcional): filtra profissionais que compartilham zonas com o profissional informado.

### Response 200

```json
[
  {
    "id": "prof_1",
    "userId": "user_1",
    "name": "Ana Souza",
    "avatarUrl": "https://cdn.exemplo/avatar.png",
    "rating": 4.9,
    "active": true,
    "completedJobsCount": 42,
    "availabilityText": "Disponível hoje",
    "services": [
      {
        "id": "ps_1",
        "serviceId": "srv_1",
        "name": "Manicure",
        "price": 80,
        "description": "Atendimento em domicílio"
      }
    ],
    "zones": ["zona-norte", "zona-oeste"]
  }
]
```

### Observações

- `services` e `zones` nunca retornam `null` (retornam lista vazia quando não houver itens).
- O endpoint aplica cache in-memory por 60s (bypass com header `Cache-Control: no-cache`).
- A consulta evita N+1:
  - 1 query base de profissionais + usuário
  - 1 query de serviços em lote (`ANY(@professionalIds)`)
  - 1 query de zonas em lote (`ANY(@professionalIds)`)
