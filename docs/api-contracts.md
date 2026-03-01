# API Contracts

## GET /professionals
Retorna profissionais já enriquecidos para render da home (sem necessidade de chamadas adicionais para zonas/serviços).

### Query params (opcional)
- `zoneId`
- `serviceId`

### Headers
- `Cache-Control: no-cache` para bypass de cache.

### Response 200
```json
[
  {
    "id": "string",
    "userId": "string",
    "name": "string",
    "avatarUrl": "string|null",
    "rating": 4.8,
    "active": true,
    "completedJobsCount": 120,
    "availabilityText": "Hoje 14:00-18:00",
    "services": [
      {
        "id": "string",
        "serviceId": "string",
        "name": "Corte",
        "price": 45.0,
        "description": "Descrição opcional",
        "icon": "scissors"
      }
    ],
    "zones": [
      {
        "id": "string",
        "name": "Zona Sul"
      }
    ]
  }
]
```

## GET /zones
Retorna zonas ativas para filtros.

### Headers
- `Cache-Control: no-cache` para bypass de cache.

### Response 200
```json
[
  { "id": "string", "name": "Centro" }
]
```

## GET /services
Retorna serviços para filtros/listagens.

### Headers
- `Cache-Control: no-cache` para bypass de cache.

### Response 200
```json
[
  { "id": "string", "name": "Encanador", "icon": "wrench" }
]
```

## GET /bootstrap
Endpoint único recomendado para Home.

### Headers
- `Cache-Control: no-cache` para bypass de cache.

### Response 200
```json
{
  "professionals": [],
  "zones": [],
  "services": []
}
```

## GET /home/bootstrap
Alias que redireciona para `/bootstrap`.
