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

## Trust fields (PRD-Contrato-Dados-Confianca)

Os cinco campos abaixo (`verificationStatus`, `badges`, `responseRate`,
`avgResponseTimeMinutes`, `completionRate`) são colunas diretas da tabela
`Professional` (sem join com tabela de métricas separada — ver
`artifacts/sql/phase5_verification_trust_metrics.sql`) e aparecem
identicamente em três lugares:

- `GET /professionals` (listagem, `ProfessionalCardDto`)
- `GET /professionals/{id}` (perfil detalhado)
- `GET /professionals/{id}/trust-metrics` (endpoint dedicado, retorna só esses 5 campos)

Antes desta revisão, `GET /professionals/{id}` e `GET /professionals/{id}/trust-metrics`
**não incluíam esses campos** (o repositório de detalhe usava uma projeção
separada da listagem, que já divergia). Ambos foram corrigidos para ler das
mesmas colunas de `Professional` que a listagem usa — não há mais duas fontes
de cálculo para reconciliar.

| Campo | Tipo | Nullable | Regra de exibição sugerida |
|---|---|---|---|
| `verificationStatus` | `string` | não (default `"pending"`) | Badge "Verificado" só quando `"verified"`. |
| `completionRate` | `number \| null` | sim | Ocultar se nulo. O backend já retorna `null` quando a amostra é pequena (ver decisão abaixo) — não é necessário replicar o limiar no frontend. |
| `avgResponseTimeMinutes` | `number \| null` | sim | Ocultar se nulo. |
| `responseRate` | `number \| null` | sim | Ocultar se nulo. |
| `badges` | `string[]` | não (default `[]`) | — |

### Frequência de atualização (Item 1.1)

`TrustMetricsService.RecalculateAsync`/`RecalculateAllAsync`
(`src/Infrastructure/Services/TrustMetricsService.cs`) é a única fonte de
cálculo. É acionado por três caminhos:

1. `TrustMetricsJob` (`src/Infrastructure/BackgroundJobs/TrustMetricsJob.cs`) —
   `BackgroundService` com `PeriodicTimer` de 24h, roda `RecalculateAllAsync`
   in-process.
2. `POST /internal/jobs/trust-metrics` — endpoint manual/externo (aceita
   `professionalId` opcional para recálculo único; sem parâmetro, dispara
   `RecalculateAllAsync` em background e responde `202 Accepted`).
3. Não há recálculo on-write (revisão concluída, pedido finalizado etc.) —
   os valores só mudam quando um dos dois caminhos acima roda.

**Risco identificado:** a stack roda em AWS Lambda
(`infra/sam/template.yaml`), que não garante processos in-memory de longa
duração — o `PeriodicTimer` de 24h do `TrustMetricsJob` não tem garantia de
execução real em produção (Lambda pode reciclar a instância antes do timer
disparar). O template SAM atual **não define nenhuma regra EventBridge**
chamando `POST /internal/jobs/trust-metrics` na Lambda. Até que esse
agendamento externo seja configurado, os campos de confiança podem ficar
desatualizados por período indefinido, ou nulos para profissionais
recém-cadastrados até o primeiro recálculo (manual ou pela próxima janela em
que a instância ficar quente por 24h contínuas, o que não é garantido).
Ação recomendada fora do escopo deste PRD: criar uma regra EventBridge
Schedule (ex.: diária) no `template.yaml` chamando
`POST /internal/jobs/trust-metrics`.

### Decisão — `completionRate` para amostras pequenas (Item 1.4)

**Aceita e implementada.** `TrustMetricsService.CalculateCompletionRateAsync`
retorna `null` quando o profissional tem menos de 3 pedidos que chegaram à
fase de execução (`scheduled` ou além), em vez de calcular uma taxa sobre
amostra estatisticamente vazia (ex.: 1/1 = 100%). O limiar é a constante
`MinSampleForCompletionRate` no serviço. O frontend não precisa replicar essa
regra — quando `completionRate` vier `null`, é para ocultar o sinal, seja por
ausência de dados ou por amostra pequena demais.
