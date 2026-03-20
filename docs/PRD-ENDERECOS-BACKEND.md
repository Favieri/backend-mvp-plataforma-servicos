# PRD BACK-END — Endereço padrão do cliente e endereço do serviço vinculado ao pedido

## Contexto
A plataforma agora gira em torno de um **pedido**.

Hoje existe um gap funcional importante:
- o cliente não informa endereço no cadastro
- quando um serviço é fechado/agendado, o profissional não visualiza o endereço do atendimento
- o cliente pode querer contratar o serviço para um endereço diferente do endereço padrão da conta

Precisamos suportar:
1. **Endereço padrão do cliente** no cadastro/perfil
2. **Endereço do serviço** vinculado ao pedido fechado/agendado
3. Possibilidade de, ao aceitar proposta no chat ou agendar serviço diretamente, o cliente:
   - usar o endereço padrão
   - ou informar um endereço específico naquele momento

---

## Objetivo
Implementar no back-end a estrutura e as regras para:

- armazenar o **endereço padrão** do cliente
- armazenar no pedido um **snapshot do endereço do serviço**
- permitir que o fechamento do pedido use:
  - o endereço padrão do cliente
  - ou um endereço alternativo informado naquele momento
- retornar esse endereço para as telas do cliente e do profissional

---

## Decisão funcional
### 1. Endereço padrão da conta
Cada cliente terá **1 endereço padrão** salvo no cadastro/perfil.

### 2. Endereço do serviço
Cada pedido fechado/agendado deve armazenar um **snapshot próprio do endereço do serviço**.

Esse snapshot não deve depender dinamicamente do endereço atual do cliente, pois:
- o cliente pode alterar o endereço padrão depois
- pedidos antigos precisam manter o endereço original usado no atendimento

### 3. Sem múltiplos endereços salvos nesta fase
Nesta entrega, **não é necessário implementar uma carteira de múltiplos endereços salvos**.

Escopo desta fase:
- 1 endereço padrão na conta
- 1 endereço do serviço por pedido

---

## Requisitos funcionais

### RF01 — Cadastro/perfil do cliente com endereço padrão
O back-end deve permitir criar e atualizar o endereço padrão do cliente.

### RF02 — Aceite de proposta com definição de endereço
Ao aceitar uma proposta no chat, o cliente deve poder informar qual endereço será usado no serviço.

### RF03 — Agendamento/fechamento direto com definição de endereço
Ao fechar/agendar um pedido fora do fluxo de proposta, o sistema deve igualmente exigir a definição do endereço do serviço.

### RF04 — Snapshot do endereço no pedido
Quando o pedido for fechado/agendado, o endereço escolhido deve ser salvo no pedido como snapshot.

### RF05 — Exibição para o profissional
O endereço do serviço deve estar disponível nos endpoints consumidos pela área do profissional para pedidos fechados/agendados/aceitos.

---

## Modelagem de dados

## Estrutura de endereço
Usar a mesma estrutura lógica para:
- endereço padrão do cliente
- endereço do serviço no pedido

Campos mínimos:
- `zipCode` / `cep`
- `street` / `logradouro`
- `number`
- `neighborhood` / `bairro`
- `city`
- `state`
- `complement` (opcional)
- `reference` (opcional, se fizer sentido no domínio)
- `country` (opcional; default BR se a plataforma hoje for Brasil-only)

### Observação
O campo **number** deve continuar manual.  
CEP não fornece número.

---

## Persistência
Implementar com **menor impacto possível na arquitetura atual**, respeitando o modelo existente.

### Obrigatório do ponto de vista de domínio
O sistema deve suportar dois conjuntos de dados distintos:

#### A. Endereço padrão do cliente
Persistido no agregado/perfil do cliente (ou equivalente do usuário cliente).

#### B. Endereço do serviço
Persistido no pedido como snapshot imutável do momento do fechamento/agendamento.

### Se a arquitetura permitir
Preferência por uma destas abordagens:
1. **Value object / owned type / embedded fields**
2. **Tabela/address entity própria**
3. **Campos diretamente na entidade**, se isso for o caminho de menor impacto

A escolha é técnica, mas o comportamento funcional acima é obrigatório.

---

## Regras de negócio

### RN01 — Endereço padrão obrigatório para fluxo com uso do padrão
Se `useDefaultAddress = true`, o cliente precisa ter endereço padrão cadastrado.

Caso não tenha:
- retornar erro de validação claro
- não concluir o aceite/agendamento

### RN02 — Endereço alternativo exige payload completo
Se `useDefaultAddress = false`, o payload deve trazer os campos mínimos obrigatórios do endereço do serviço.

### RN03 — Snapshot obrigatório no fechamento do pedido
Nenhum pedido deve ser marcado como aceito/agendado/fechado sem endereço do serviço definido.

### RN04 — Alterar o endereço padrão depois não altera pedidos já fechados
Pedidos já aceitos/agendados devem manter o endereço salvo naquele momento.

### RN05 — Endereço visível ao profissional somente no contexto correto
O endereço do serviço deve estar disponível nas consultas do profissional para pedidos aceitos/agendados/fechados.

---

## Contratos de API

## 1. Cadastro/atualização de perfil do cliente
Adaptar os endpoints existentes de cadastro/perfil para aceitar endereço padrão.

### Exemplo de payload
```json
{
  "name": "Nome do cliente",
  "phone": "11999999999",
  "defaultAddress": {
    "zipCode": "01310100",
    "street": "Avenida Paulista",
    "number": "1000",
    "neighborhood": "Bela Vista",
    "city": "São Paulo",
    "state": "SP",
    "complement": "Apto 101",
    "reference": "Próximo ao metrô"
  }
}
```

### Observação
Se o fluxo atual separa cadastro e edição de perfil, adaptar ambos os contratos relevantes.

---

## 2. Leitura do perfil do cliente
Os endpoints que retornam dados do cliente logado devem incluir `defaultAddress`.

---

## 3. Aceite de proposta
O endpoint de aceite de proposta deve passar a receber a definição do endereço do serviço.

### Exemplo
`POST /proposals/{proposalId}/accept`

### Payload sugerido
```json
{
  "useDefaultAddress": true,
  "serviceAddress": null
}
```

ou

```json
{
  "useDefaultAddress": false,
  "serviceAddress": {
    "zipCode": "22790000",
    "street": "Rua Exemplo",
    "number": "250",
    "neighborhood": "Barra da Tijuca",
    "city": "Rio de Janeiro",
    "state": "RJ",
    "complement": "Casa 2",
    "reference": "Portão azul"
  }
}
```

### Comportamento esperado
- `useDefaultAddress = true`
  - copiar endereço padrão do cliente para o pedido
- `useDefaultAddress = false`
  - validar `serviceAddress`
  - salvar `serviceAddress` no pedido

---

## 4. Fluxo de agendamento/fechamento direto
Todo endpoint que hoje conclui/agende/crie um pedido fechado deve aceitar a mesma regra de endereço.

Se houver mais de um endpoint com essa responsabilidade, padronizar o contrato.

---

## 5. Endpoints de listagem/detalhe de pedido
Os endpoints usados por:
- cliente
- profissional
- chat/detalhe do pedido

devem retornar o endereço do serviço quando o pedido já estiver fechado/agendado/aceito.

### Exemplo de retorno
```json
{
  "id": "pedido-id",
  "status": "scheduled",
  "serviceAddress": {
    "zipCode": "22790000",
    "street": "Rua Exemplo",
    "number": "250",
    "neighborhood": "Barra da Tijuca",
    "city": "Rio de Janeiro",
    "state": "RJ",
    "complement": "Casa 2",
    "reference": "Portão azul"
  }
}
```

---

## Validações

### CEP
- obrigatório
- aceitar apenas formato válido
- idealmente normalizar para somente números internamente

### Campos obrigatórios do endereço
Obrigatórios:
- zipCode
- street
- number
- neighborhood
- city
- state

Opcionais:
- complement
- reference

### Erros
Retornar mensagens claras, por exemplo:
- `Cliente não possui endereço padrão cadastrado`
- `Endereço do serviço é obrigatório para concluir o pedido`
- `CEP inválido`
- `Campo number é obrigatório`

---

## Compatibilidade e impacto
Objetivo é **menor impacto possível**.

Diretrizes:
- preservar endpoints atuais quando possível
- só evoluir contratos onde necessário
- evitar duplicação de regra em múltiplos services/use cases
- centralizar a resolução do endereço do serviço em um ponto único do domínio/aplicação

---

## Casos de teste

### Cenário 1 — Cliente salva endereço padrão
- cadastrar/editar cliente com endereço padrão
- buscar perfil
- validar retorno do endereço salvo

### Cenário 2 — Aceitar proposta com endereço padrão
- cliente com endereço padrão cadastrado
- aceitar proposta com `useDefaultAddress = true`
- pedido deve ser concluído com snapshot do endereço padrão

### Cenário 3 — Aceitar proposta com endereço alternativo
- cliente aceita proposta com `useDefaultAddress = false`
- envia `serviceAddress`
- pedido deve ser concluído com snapshot do endereço alternativo

### Cenário 4 — Cliente sem endereço padrão tentando usar endereço padrão
- `useDefaultAddress = true`
- sem endereço no perfil
- backend deve bloquear com erro de validação

### Cenário 5 — Alterar endereço padrão depois
- cliente fecha pedido usando endereço padrão
- depois altera endereço padrão no perfil
- pedido antigo deve permanecer com o endereço original

### Cenário 6 — Profissional visualiza endereço do serviço
- pedido aceito/agendado
- profissional acessa listagem/detalhe
- endereço do serviço aparece corretamente

---

## Critérios de aceite
- cliente possui endereço padrão no cadastro/perfil
- aceite de proposta exige definição do endereço do serviço
- agendamento/fechamento direto também exige definição do endereço do serviço
- pedido salvo contém snapshot do endereço usado
- profissional consegue visualizar esse endereço
- alteração futura no perfil do cliente não altera o endereço de pedidos já fechados

---

## Entregáveis esperados do agente
1. diagnóstico da modelagem e pontos alterados
2. lista de arquivos alterados
3. migrations/schema updates
4. contratos atualizados
5. correção/implementação completa
6. passo a passo de teste manual
