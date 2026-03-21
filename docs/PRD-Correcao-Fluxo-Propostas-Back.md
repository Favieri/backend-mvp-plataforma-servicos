<!--
Convertido de: PRD Correção Fluxo Propostas - Back.pdf
Observação: o conteúdo do PDF trata de login social com Google e Facebook.
-->

# PRD BACK-END — Login com Google e Facebook

## Contexto

A plataforma hoje utiliza login e cadastro via e-mail e senha (Supabase Auth e endpoint `/auth`). Para clientes, o cadastro exige selecionar uma zona (`zoneId`) e informar um endereço padrão. Os endereços de serviço são definidos somente quando o pedido é fechado, conforme o PRD de endereços em vigor.

Para reduzir o atrito na entrada, queremos permitir que usuários se autentiquem com Google ou Facebook, simplificando o acesso e evitando que tenham de criar uma senha logo no início.

## Objetivo

Implementar no back-end suporte a login social por Google e Facebook. Esse login deve funcionar tanto para usuários existentes, vinculando o provedor à conta, quanto para novos usuários, criando a conta automaticamente.

O backend deverá:

- validar os tokens;
- criar ou localizar o usuário;
- gerar o mesmo tipo de JWT já utilizado pela API;
- retornar o usuário com os campos relevantes.

Como o PRD de endereços estabelece que `zoneId` e `defaultAddress` são obrigatórios para fechar um pedido, mas não necessariamente no momento do login social, esses campos poderão permanecer nulos até que o usuário conclua o cadastro posteriormente.

## Requisitos funcionais

### RF1 — Autenticação com Google

- Criar o endpoint `POST /auth/google`.
- O corpo deve aceitar um objeto JSON contendo pelo menos `idToken`.
- Esse token é o JWT emitido pelo Google após o usuário se autenticar via Google Identity Services.

O backend deve validar o token com o Google, garantindo que:

- o token está assinado corretamente e ainda é válido;
- o `aud` corresponde ao `GOOGLE_CLIENT_ID` configurado na aplicação;
- o e-mail do usuário no token está verificado (`email_verified = true`);
- em caso negativo, deve retornar erro.

A partir do token, extrair: `sub`, `email`, `name` e demais campos úteis.

Fluxo de persistência:

- Verificar se já existe um usuário com `provider = "google"` e `providerUserId = sub`. Se existir, retornar esse usuário sem criar outro.
- Caso não exista, verificar se já existe um usuário com o mesmo e-mail.
- Se existir e não possuir provedor associado, vincular o provedor Google, preenchendo `provider` e `providerUserId`, e retorná-lo.
- Se não existir usuário com esse e-mail, criar um novo usuário com:
  - `name` e `email` conforme retornado pelo token;
  - `role` padrão como `cliente`;
  - `password` em branco ou nula;
  - `zoneId` e `defaultAddress` como `null`;
  - `provider = "google"` e `providerUserId = sub`.

Após localizar ou criar o usuário:

- gerar um JWT da mesma forma que o endpoint atual `/auth`, com campos `sub`, `email`, `role` e expiração de 7 dias;
- continuar assinando com `SUPABASE_JWT_SECRET`;
- responder com `{ token, user }` no mesmo formato do endpoint `/auth`, incluindo os campos novos `provider` e `providerUserId`.

### RF2 — Autenticação com Facebook

- Criar o endpoint `POST /auth/facebook`.
- O corpo deve aceitar um objeto JSON contendo `accessToken`.
- Dependendo da implementação, também pode aceitar `userID` ou `code`.

O backend deve validar o token chamando a Graph API do Facebook:

- `https://graph.facebook.com/me?fields=id,name,email&access_token=...`

Além disso, deve assegurar que o `accessToken` pertence ao app configurado (`FACEBOOK_APP_ID`). Para produção, recomenda-se também chamar `/debug_token` com `appId` e `appSecret` para garantir que o token foi emitido para o aplicativo correto.

Depois, extrair `id`, `email` e `name`.

Regra importante:

- Se não existir `email` no retorno, retornar um erro informando que a conta do Facebook não forneceu e-mail e sugerir outro método de login.

Demais regras:

- Seguir a mesma lógica de criação e vinculação descrita para o Google, utilizando `provider = "facebook"` e `providerUserId = id`.
- Gerar o JWT e retornar `{ token, user }`.

### RF3 — Estrutura de dados

- Adicionar os campos `provider` (string) e `providerUserId` (string) à entidade ou tabela `User`.
- Esses campos podem ser nulos para usuários criados por e-mail/senha.
- Uma alternativa seria criar uma tabela `UserIdentityProviders (userId, provider, providerUserId)` referenciando `User`, mas, para simplicidade e baixo impacto, os campos embutidos na tabela `User` são suficientes nesta fase.
- Garantir unicidade de `(provider, providerUserId)` e manter unicidade de `email`.

### RF4 — Usuários sociais sem `zoneId` ou `defaultAddress`

- Ajustar o código de criação de usuários para permitir que `zoneId` e `defaultAddress` sejam `null` quando `provider` não for nulo.
- A validação atual em `/users` que exige `zoneId` para clientes não deve ser aplicada dentro de `/auth/google` e `/auth/facebook`.
- Manter a regra de que, ao tentar fechar ou agendar um pedido, o backend deve validar que o usuário possui `zoneId` e, quando aplicável, `defaultAddress`, retornando erros claros caso contrário, conforme o PRD de endereços.

### RF5 — APIs para completar o perfil

Reutilizar os endpoints existentes para atualizar zona e endereço:

- `PUT /users/{id}` para atualizar `zoneId` e outros campos de usuário.
- `PUT /users/{id}/default-address` para atualizar o endereço padrão.

Esses endpoints já existem e validam o endereço conforme a regra atual.

## Requisitos técnicos

- Incluir novas variáveis de ambiente: `GOOGLE_CLIENT_ID`, `FACEBOOK_APP_ID` e, se necessário, `FACEBOOK_APP_SECRET`.
- Utilizar bibliotecas oficiais ou chamadas HTTP para validar tokens.
  - Google: `GoogleJsonWebSignature.ValidateAsync` (biblioteca `Google.Apis.Auth`) ou a rota `tokeninfo`.
  - Facebook: chamada à Graph API `/debug_token` ou `/me` com `fields=id,name,email`.
- Gerar o JWT usando a mesma chave secreta (`SUPABASE_JWT_SECRET`) e o mesmo esquema de claims do endpoint `/auth` existente.

## Detalhamento técnico e exemplos de código

Esta seção apresenta sugestões práticas de implementação e referências à documentação oficial para os provedores.

### Validação do `idToken` do Google em C#

Para validar o `idToken` recebido do front, é possível utilizar a biblioteca oficial `Google.Apis.Auth`.

```csharp
using Google.Apis.Auth;

public class GoogleAuthService
{
    private readonly string _googleClientId;

    public GoogleAuthService(IConfiguration config)
    {
        _googleClientId = config["GOOGLE_CLIENT_ID"];
    }

    public async Task<(bool isValid, string sub, string email, string name)> ValidateAsync(string idToken)
    {
        var settings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = new[] { _googleClientId }
        };

        var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings);

        // Verifica se email está verificado
        if (!payload.EmailVerified)
            return (false, null, null, null);

        return (true, payload.Subject, payload.Email, payload.Name);
    }
}
```

Esse código utiliza `ValidateAsync` para decodificar e validar a assinatura do token, garantindo que o campo `aud` corresponde ao `GOOGLE_CLIENT_ID`. O campo `payload.EmailVerified` indica se o usuário confirmou o e-mail na conta Google.

Como fallback ou para depuração, também é possível validar o token manualmente chamando:

```text
GET https://oauth2.googleapis.com/tokeninfo?id_token=<ID_TOKEN>
```

Essa rota retorna as claims do token em formato JSON. Use-a apenas como fallback ou para depuração, pois há limites de taxa.

### Validação do `accessToken` do Facebook em C#

O token do Facebook não é um JWT; ele deve ser verificado via Graph API. Um fluxo típico envolve duas chamadas.

#### 1. Debug do token

```csharp
// GET https://graph.facebook.com/debug_token?input_token={accessToken}&access_token={appId}|{appSecret}
var result = await _httpClient.GetFromJsonAsync<DebugTokenResponse>(
    $"https://graph.facebook.com/debug_token?input_token={accessToken}&access_token={_appId}|{_appSecret}");

if (!result.Data.IsValid || result.Data.AppId != _appId)
{
    throw new Exception("AccessToken inválido ou emitido para outro app");
}
```

#### 2. Obter dados do usuário

```csharp
// GET https://graph.facebook.com/me?fields=id,name,email&access_token={accessToken}
var userInfo = await _httpClient.GetFromJsonAsync<FacebookUserResponse>(
    $"https://graph.facebook.com/me?fields=id,name,email&access_token={accessToken}");

if (string.IsNullOrWhiteSpace(userInfo.Email))
{
    throw new Exception("Conta do Facebook não forneceu e-mail. Permissão email é obrigatória.");
}
```

Os modelos `DebugTokenResponse` e `FacebookUserResponse` são classes simples para desserializar o JSON retornado.

### Exemplo de criação ou vinculação de usuário

Após validar o token e extrair os campos (`sub` ou `id`, `email` e `name`), implementar a lógica de criação ou vinculação:

```csharp
// Pseudocódigo
// 1. Buscar usuário por providerUserId
var user = await _userRepository.FindByProviderAsync(provider, providerUserId);
if (user != null)
    return user;

// 2. Buscar por e-mail
user = await _userRepository.FindByEmailAsync(email);
if (user != null)
{
    // Se usuário existe mas não tem provider associado, vincular
    user.Provider = provider;
    user.ProviderUserId = providerUserId;
    await _userRepository.UpdateAsync(user);
    return user;
}

// 3. Criar novo usuário com dados básicos e campos nulos
user = new User
{
    Name = name,
    Email = email,
    Role = Role.Cliente,
    Password = null, // login social
    ZoneId = null,
    DefaultAddress = null,
    Provider = provider,
    ProviderUserId = providerUserId
};

await _userRepository.CreateAsync(user);
return user;
```

Esse pseudocódigo mostra o fluxo condicional necessário para reutilizar contas existentes ou criar novas sem duplicação. Certifique-se de aplicar índices únicos em `(provider, providerUserId)` e `email` no banco.

## Convenções de código e tratamento de erros

- Retorne sempre mensagens claras no padrão JSON `{ "error": "mensagem" }` quando o token estiver ausente, inválido, expirado ou não contiver e-mail.
- Defina tempos de expiração adequados para o JWT, por exemplo, 7 dias.
- Considere refresh tokens em fases futuras.
- Mantenha isolada a lógica de validação de tokens em serviços específicos, por exemplo `GoogleAuthService` e `FacebookAuthService`, para facilitar testes e manutenção.

## Documentação de referência

- Google Identity: consultar a documentação oficial do Google sobre verificação do ID Token no servidor.
- Facebook Login: consultar a página de tokens de acesso do Facebook Login.

Essas referências contêm detalhes sobre claims, escopos, validação e boas práticas de segurança.

## Contratos de API

### `POST /auth/google`

**Entrada:**

```json
{
  "idToken": "string"
}
```

**Saída (sucesso):**

```json
{
  "token": "jwt",
  "user": {
    "id": "...",
    "name": "Nome do usuário",
    "email": "usuario@gmail.com",
    "role": "cliente",
    "zoneId": null,
    "defaultAddress": null,
    "provider": "google",
    "providerUserId": "1234567890"
  }
}
```

**Erros comuns:**

- `400` — campo `idToken` ausente ou inválido.
- `400` — e-mail não verificado.
- `500` — falha ao validar token.

### `POST /auth/facebook`

**Entrada:**

```json
{
  "accessToken": "string"
}
```

**Saída e erros:** idênticos ao endpoint Google, com a diferença de `provider` ser `"facebook"`.

## Fluxos de usuário

### Login Google

1. O usuário clica em **Continuar com Google** no front.
2. O front obtém um `idToken` do Google.
3. Chama `POST /auth/google` com `{ idToken }`.
4. O backend valida o token, cria ou vincula usuário e retorna token e dados.
5. O front salva o token e o usuário no `localStorage`.
6. Se `zoneId` ou `defaultAddress` forem nulos, o front direciona o usuário para completar o perfil antes de fechar ou agendar serviços.

### Login Facebook

Processo idêntico, usando `accessToken` e o endpoint `POST /auth/facebook`.

## Casos de teste

- **Usuário novo com Google:** fornecer `idToken` válido e verificar que um novo usuário é criado com `provider=google`, `zoneId=null` e `defaultAddress=null`.
- **Usuário existente (e-mail e senha) usa Google:** fornecer token com e-mail existente e verificar que os campos `provider` e `providerUserId` são preenchidos e que nenhuma senha é alterada.
- **Usuário social tenta fechar pedido sem endereço:** o endpoint de criação de pedido deve retornar erro indicando falta de `zoneId` ou endereço, conforme PRD de endereços.
- **Usuário novo com Facebook sem e-mail:** chamar com `accessToken` de conta sem e-mail autorizado; deve retornar erro apropriado.

## Critérios de aceite

- Endpoints `/auth/google` e `/auth/facebook` implementados e disponíveis.
- Validação correta dos tokens, incluindo issuer, audience, expiração e e-mail verificado.
- Usuários sociais persistem `provider` e `providerUserId` sem impactar usuários por senha.
- Usuários sociais podem ter `zoneId` e `defaultAddress` nulos sem impedir login.
- JWT retornado funciona nos demais endpoints protegidos, como `/conversations` e `/orders`.
- Documentação e código de migração de banco de dados atualizados.
- Relatório de arquivos modificados e guia de testes manuais preparado pelo agente.
