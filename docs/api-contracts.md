# Contratos de API — `GET /professionals`

Referência rápida do formato retornado por `GET /professionals` (e demais
endpoints que reaproveitam `ProfessionalCardDto`), para alinhar backend e
frontend sem precisar ler o DTO C# toda vez.

## `ProfessionalCardDto` (item do array retornado por `GET /professionals`)

Serialização usa `camelCase` (padrão do Minimal API / System.Text.Json).

| Campo JSON              | Tipo               | Observações                                                                 |
|--------------------------|--------------------|------------------------------------------------------------------------------|
| `id`                     | `string`           | Id do profissional.                                                          |
| `userId`                 | `string`           | Id do usuário associado.                                                     |
| `name`                   | `string`           |                                                                                |
| `avatarUrl`              | `string \| null`   |                                                                                |
| `rating`                 | `number \| null`   | Média das avaliações.                                                        |
| `reviewCount`            | `number`           | **Novo.** Contagem de avaliações que formam `rating`. `0` quando não há avaliações. Batched via `GROUP BY ProfessionalId` — sem N+1. |
| `active`                 | `boolean`          |                                                                                |
| `completedJobsCount`     | `number \| null`   |                                                                                |
| `availabilityText`       | `string \| null`   |                                                                                |
| `services`               | `ProfessionalServiceDto[]` |                                                                        |
| `zones`                  | `ZoneDto[]`        |                                                                                |
| `verificationStatus`     | `string`           | Ex.: `"pending"`, `"verified"`. **Não é booleano** — não existe campo `verified`. Frontend deve derivar com `verificationStatus === "verified"` se precisar de um booleano. |
| `badges`                 | `string[]`         | Lista de códigos de badge (pode ser vazia).                                  |
| `responseRate`           | `number \| null`   |                                                                                |
| `avgResponseTimeMinutes` | `number \| null`   |                                                                                |
| `completionRate`         | `number \| null`   |                                                                                |

### Decisão — campo `verified` booleano (PRD-frontend-ux-confianca, Item 1)

Não foi adicionado um campo `verified` booleano ao DTO. O frontend deve
derivar esse valor a partir de `verificationStatus === "verified"` no seu
adaptador. Caso o time de frontend prefira que o backend exponha o booleano
diretamente, atualizar esta seção e o `ProfessionalCardDto` juntos para
evitar divergência de contrato.

### Fonte da contagem de avaliações

`reviewCount` é calculado com `COUNT(*)` sobre a tabela `Review` agrupado por
`professionalId`, na mesma query batched usada para `services`/`zones`
(uma query para todos os profissionais da página, não uma por profissional).
Não há filtro de visibilidade (`clientVisibleAt`/`professionalVisibleAt`) —
mesmo critério usado hoje pelo endpoint de perfil (`GET /reviews`), então os
números são coerentes entre listagem e perfil individual.
