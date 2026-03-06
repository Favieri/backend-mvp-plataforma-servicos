# PRD — Migração de acesso a dados para Entity Framework Core em projeto .NET 8

## 1) Objetivo

Migrar o acesso a dados de um projeto **.NET 8** que hoje usa **queries diretas** dentro dos repositórios para uma implementação baseada em **Entity Framework Core (EF Core)**, preservando o padrão de repositório **somente onde ele já faz parte do contrato da aplicação**, mas substituindo SQL manual por:

- `DbContext`
- `DbSet<TEntity>`
- consultas LINQ
- mapeamentos via Fluent API
- migrações versionadas
- estratégias explícitas de performance e segurança

A implementação deve ser feita de forma **incremental**, **segura**, **testável** e **reversível**, minimizando regressão funcional.

---

## 2) Contexto e racional técnico

O EF Core é o ORM oficial do ecossistema .NET moderno para acesso relacional e elimina grande parte do código repetitivo de acesso a dados. O `DbContext` já representa, por si só, uma combinação de **Unit of Work** e **Repository**. Portanto:

- **não é necessário** criar uma abstração genérica extra “por cima” do EF apenas por padrão
- **mas** se o projeto já expõe interfaces de repositório consumidas por serviços/casos de uso, essas interfaces podem e devem ser **preservadas** para reduzir churn arquitetural

### Diretriz principal

**Preservar contratos públicos, simplificar a implementação interna.**

Ou seja:

- manter `IUserRepository`, `IOrderRepository`, etc., se já existirem e forem usados pela aplicação
- reimplementar esses repositórios usando EF Core
- evitar “generic repository” excessivamente abstrato se ele não agregar regra real
- permitir SQL manual **somente** quando:
  - houver limitação real de tradução LINQ
  - houver necessidade comprovada de performance
  - ou houver integração com recurso específico do banco
- quando SQL bruto for inevitável, ele deve ser **parametrizado** e documentado

---

## 3) Escopo

### Incluído

1. Introdução do EF Core ao projeto
2. Definição do `DbContext`
3. Mapeamento das entidades e relacionamentos
4. Refatoração dos repositórios para usar `DbContext`/LINQ
5. Configuração de DI
6. Criação e versionamento de migrações
7. Scripts de migração para ambientes
8. Testes automatizados de repositório e integração
9. Checklist de performance
10. Checklist de segurança
11. Documentação operacional para manutenção futura

### Não incluído

1. Reescrita completa da camada de domínio
2. Mudança desnecessária de contratos da aplicação
3. Adoção de lazy loading por padrão
4. Reestruturação total da arquitetura sem necessidade
5. Otimizações prematuras sem benchmark

---

## 4) Resultado esperado

Ao final da entrega, o projeto deve:

- compilar e executar com EF Core
- substituir queries manuais dos repositórios por LINQ/EF Core
- manter o comportamento funcional existente
- ter migrações geradas e aplicáveis
- ter testes cobrindo cenários críticos
- ter scripts de banco para desenvolvimento e produção
- seguir regras claras de performance, rastreamento e segurança
- evitar regressão de transação, concorrência e consistência

---

## 5) Decisões arquiteturais obrigatórias

## 5.1 Versão e compatibilidade

Para um projeto em **.NET 8**, usar por padrão **EF Core 8.x** e o **provider na mesma major version**.

Exemplos:

- SQL Server: `Microsoft.EntityFrameworkCore.SqlServer` **8.x**
- PostgreSQL: `Npgsql.EntityFrameworkCore.PostgreSQL` **8.x**
- SQLite (principalmente testes ou casos específicos): `Microsoft.EntityFrameworkCore.Sqlite` **8.x**

> Regra: **todos os pacotes EF devem estar alinhados na mesma major version**.

### Pacotes base

```bash
dotnet add package Microsoft.EntityFrameworkCore --version 8.*
dotnet add package Microsoft.EntityFrameworkCore.Design --version 8.*
dotnet add package Microsoft.EntityFrameworkCore.Tools --version 8.*
```

### Exemplo para o PostgreSQL

```bash
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 8.*
```

### Se houver testes com SQLite

```bash
dotnet add package Microsoft.EntityFrameworkCore.Sqlite --version 8.*
```

---

## 5.2 Organização recomendada de projetos

Estrutura preferencial (MANTER A JÁ EXISTENTE):

```text
src/
  MyApp.Api
  MyApp.Application
  MyApp.Domain
  MyApp.Infrastructure
tests/
  MyApp.Infrastructure.Tests
  MyApp.Api.IntegrationTests
```

### Local recomendado para EF Core

- `DbContext`: `Infrastructure`
- `IEntityTypeConfiguration<>`: `Infrastructure/Persistence/Configurations`
- `Migrations`: `Infrastructure/Persistence/Migrations`
- repositórios concretos: `Infrastructure/Repositories`

### Observação

Se o projeto já tem uma separação madura, respeitar a estrutura existente. Não mover arquivos sem necessidade.

---

## 5.3 Estratégia de migração incremental

O agente **não** deve tentar fazer um big bang.

Ordem correta:

1. Inventariar queries e repositórios existentes
2. Introduzir `DbContext` e mapeamentos
3. Migrar um agregado por vez
4. Cobrir cada agregado com testes
5. Só então remover código SQL legado daquele agregado
6. Repetir até concluir

---

## 6) Como o agente de IA deve pensar durante a implementação

## 6.1 Protocolo de execução

O agente deve seguir esta ordem mental:

### Fase A — Descoberta
- localizar todos os repositórios atuais
- catalogar quais métodos usam SQL manual
- identificar entidades, tabelas, joins, filtros, paginação e ordenação
- mapear quais serviços dependem de cada repositório
- identificar transações, locks, concorrência e SQL específico do provider
- verificar se já existe algum ORM parcial no projeto

### Fase B — Baseline
- preservar contratos públicos
- criar ou reforçar testes de comportamento antes de mudar a implementação
- registrar consultas críticas e comportamento esperado

### Fase C — Modelagem
- criar as entidades EF ou adaptar as existentes
- configurar chaves primárias, relacionamentos, índices, tamanhos, nullability, precisão numérica, nomes de colunas e tabelas
- configurar conversões de tipo quando necessário

### Fase D — Substituição segura
- reimplementar o repositório com EF Core
- trocar SQL manual por LINQ
- usar projeção para DTO quando a query for de leitura
- usar `AsNoTracking()` para leitura sem update
- usar tracking apenas quando houver alteração
- validar SQL gerado nos pontos críticos

### Fase E — Banco
- gerar migração
- validar se a migração não causa perda de dados não intencional
- gerar script SQL idempotente para deploy
- manter rollback viável

### Fase F — Confiança
- rodar testes
- validar desempenho mínimo
- revisar logging, secrets e SQL injection
- remover código morto somente após validação

---

## 6.2 Regras de decisão para o agente

### Deve fazer
- preservar comportamento
- preferir LINQ claro e traduzível
- preferir Fluent API para mapeamentos não triviais
- usar consultas assíncronas
- usar `CancellationToken` quando o padrão do projeto já suportar
- separar consultas de leitura e escrita quando isso simplificar

### Não deve fazer
- expor `IQueryable` para camadas externas sem necessidade
- usar lazy loading por padrão
- chamar `ToList()` cedo demais
- carregar grafos inteiros sem necessidade
- trocar tudo para `AsNoTracking()` cegamente
- esconder falhas com `try/catch` genérico
- commitar secrets
- usar `FromSqlRaw` com concatenação de string de input
- reescrever arquitetura inteira “aproveitando a migração”

---

## 7) Passos de implementação

## 7.1 Inspecionar o estado atual

O agente deve levantar:

- lista de repositórios atuais
- lista de queries SQL atuais por método
- tabelas e views acessadas
- parâmetros, paginação, sorting, filtros dinâmicos
- stored procedures, functions e queries nativas específicas
- pontos onde há transaction scope ou transação manual
- pontos que dependem de leitura somente
- pontos que fazem update em lote

Saída esperada dessa fase:

```text
Repository -> Métodos -> SQL atual -> Entidade(s) -> Estratégia EF Core
```

---

## 7.2 Introduzir o DbContext

Exemplo:

```csharp
using Microsoft.EntityFrameworkCore;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
```

---

## 7.3 Configurar o provider e DI

### Exemplo com PostgreSQL

```csharp
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")));
```

### Observações obrigatórias

- `DbContext` deve ser **scoped** em aplicações web por padrão
- não compartilhar a mesma instância entre threads
- não armazenar `DbContext` em singleton
- uma unidade de trabalho por request/comando é o padrão

---

## 7.4 Modelagem das entidades

Exemplo:

```csharp
public sealed class Customer
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;

    private readonly List<Order> _orders = new();
    public IReadOnlyCollection<Order> Orders => _orders;
}

public sealed class Order
{
    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public decimal TotalAmount { get; private set; }

    public Customer Customer { get; private set; } = default!;

    private readonly List<OrderItem> _items = new();
    public IReadOnlyCollection<OrderItem> Items => _items;
}

public sealed class OrderItem
{
    public Guid Id { get; private set; }
    public Guid OrderId { get; private set; }
    public string ProductName { get; private set; } = string.Empty;
    public decimal UnitPrice { get; private set; }
    public int Quantity { get; private set; }

    public Order Order { get; private set; } = default!;
}
```

---

## 7.5 Mapeamento com Fluent API

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Email)
            .HasMaxLength(320)
            .IsRequired();

        builder.HasIndex(x => x.Email)
            .IsUnique();

        builder.Navigation(x => x.Orders)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TotalAmount)
            .HasPrecision(18, 2);

        builder.Property(x => x.CreatedAtUtc)
            .IsRequired();

        builder.HasOne(x => x.Customer)
            .WithMany(x => x.Orders)
            .HasForeignKey(x => x.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Items)
            .WithOne(x => x.Order)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.CustomerId, x.CreatedAtUtc });
    }
}
```

### Regras de modelagem

- preferir **Fluent API** para mapeamento persistente
- explicitar:
  - precisão decimal
  - índices
  - relacionamentos
  - delete behavior
  - nomes de tabela/coluna quando necessário
  - comprimentos máximos
- evitar confiar apenas em convenção quando o banco já existe e o schema é sensível

---

## 7.6 Refatorar repositórios

### Interface existente

```csharp
public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Order>> GetByCustomerAsync(Guid customerId, CancellationToken cancellationToken = default);
    Task AddAsync(Order order, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

### Implementação com EF Core

```csharp
using Microsoft.EntityFrameworkCore;

public sealed class OrderRepository : IOrderRepository
{
    private readonly AppDbContext _dbContext;

    public OrderRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Orders
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetByCustomerAsync(Guid customerId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Orders
            .AsNoTracking()
            .Where(x => x.CustomerId == customerId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        await _dbContext.Orders.AddAsync(order, cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return _dbContext.SaveChangesAsync(cancellationToken);
    }
}
```

### Regra importante

- queries de leitura: `AsNoTracking()` por padrão quando não houver update subsequente
- queries de escrita/edição: usar tracking
- só usar `Include` quando de fato precisar materializar a navegação
- preferir projeção para DTO em consultas de leitura

---

## 7.7 Projeção para leitura

Quando a aplicação precisa apenas de leitura, **não** carregar a entidade inteira.

```csharp
public sealed record OrderListItemDto(
    Guid Id,
    DateTime CreatedAtUtc,
    decimal TotalAmount,
    string CustomerName);

public async Task<IReadOnlyList<OrderListItemDto>> GetOrderListAsync(
    CancellationToken cancellationToken = default)
{
    return await _dbContext.Orders
        .AsNoTracking()
        .OrderByDescending(x => x.CreatedAtUtc)
        .Select(x => new OrderListItemDto(
            x.Id,
            x.CreatedAtUtc,
            x.TotalAmount,
            x.Customer.Name))
        .ToListAsync(cancellationToken);
}
```

### Regra

Para endpoints de listagem, dashboard, relatório e consulta:
- preferir `Select(...)`
- evitar materializar aggregate root completo sem necessidade

---

## 7.8 Paginação

Sempre que a consulta puder crescer:

```csharp
public async Task<IReadOnlyList<OrderListItemDto>> GetPagedAsync(
    int page,
    int pageSize,
    CancellationToken cancellationToken = default)
{
    return await _dbContext.Orders
        .AsNoTracking()
        .OrderByDescending(x => x.CreatedAtUtc)
        .Skip((page - 1) * pageSize)
        .Take(pageSize)
        .Select(x => new OrderListItemDto(
            x.Id,
            x.CreatedAtUtc,
            x.TotalAmount,
            x.Customer.Name))
        .ToListAsync(cancellationToken);
}
```

---

## 7.9 Atualizações em lote

Quando houver update/delete em lote e não for necessário carregar entidades para regra rica, considerar:

```csharp
await _dbContext.Orders
    .Where(x => x.CreatedAtUtc < cutoff)
    .ExecuteDeleteAsync(cancellationToken);
```

ou

```csharp
await _dbContext.Orders
    .Where(x => x.CustomerId == customerId)
    .ExecuteUpdateAsync(setters => setters
        .SetProperty(x => x.TotalAmount, x => x.TotalAmount * 0.95m),
        cancellationToken);
```

### Regra

Usar `ExecuteUpdateAsync` / `ExecuteDeleteAsync` apenas quando:
- a operação é realmente em lote
- não é necessário disparar lógica dependente de entidades carregadas
- os impactos de não usar o change tracker foram analisados

---

## 7.10 Consultas SQL brutas

### Política

A meta é **remover queries diretas** dos repositórios.

Ainda assim, permitir SQL bruto apenas se:

1. a query LINQ não traduzir corretamente
2. o provider exigir recurso específico
3. houver benchmark demonstrando vantagem concreta

### Forma segura

Preferir:
- `FromSql(...)`
- `FromSqlInterpolated(...)`

Evitar:
- `FromSqlRaw(...)` com string concatenada a partir de input

Exemplo aceitável:

```csharp
var orders = await _dbContext.Orders
    .FromSqlInterpolated($"SELECT * FROM orders WHERE customer_id = {customerId}")
    .ToListAsync(cancellationToken);
```

---

## 8) Transações e consistência

## 8.1 Comportamento padrão

`SaveChanges()` já usa transação por padrão quando o provider suporta.

### Regra

- para a maioria dos casos CRUD simples, confiar no comportamento padrão do EF Core
- usar transação explícita apenas quando houver múltiplos `SaveChanges`, múltiplas unidades de trabalho ou integração com outras operações dependentes

### Exemplo

```csharp
await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

try
{
    _dbContext.Orders.Add(order);
    await _dbContext.SaveChangesAsync(cancellationToken);

    _dbContext.OrderItems.Add(item);
    await _dbContext.SaveChangesAsync(cancellationToken);

    await transaction.CommitAsync(cancellationToken);
}
catch
{
    await transaction.RollbackAsync(cancellationToken);
    throw;
}
```

---

## 8.2 Concorrência

O projeto deve prever conflitos de atualização concorrente em entidades sensíveis.

### Diretriz

Adicionar token de concorrência quando necessário:
- SQL Server: `rowversion`
- outros providers: mecanismo equivalente suportado
- ou propriedade marcada como concurrency token

### Exemplo genérico

```csharp
builder.Property<byte[]>("RowVersion")
    .IsRowVersion();
```

### Regra

Em casos de conflito:
- capturar `DbUpdateConcurrencyException`
- registrar contexto do erro
- retornar resposta apropriada à camada superior
- não sobrescrever silenciosamente dados concorrentes

---

## 9) Migrações

## 9.1 Comandos base

Instalar/verificar CLI:

```bash
dotnet ef
```

Adicionar migração:

```bash
dotnet ef migrations add InitialEfMigration
```

Aplicar localmente:

```bash
dotnet ef database update
```

Gerar script SQL:

```bash
dotnet ef migrations script -o artifacts/sql/001_initial.sql
```

Gerar script idempotente:

```bash
dotnet ef migrations script --idempotent -o artifacts/sql/001_idempotent.sql
```

Verificar mudanças não migradas:

```bash
dotnet ef migrations has-pending-model-changes
```

Gerar bundle de migração:

```bash
dotnet ef migrations bundle -o artifacts/migrations/efbundle
```

---

## 9.2 Quando houver múltiplos projetos

Exemplo comum:

```bash
dotnet ef migrations add InitialEfMigration   --project src/MyApp.Infrastructure   --startup-project src/MyApp.Api   --output-dir Persistence/Migrations
```

Aplicar:

```bash
dotnet ef database update   --project src/MyApp.Infrastructure   --startup-project src/MyApp.Api
```

Gerar script idempotente:

```bash
dotnet ef migrations script --idempotent   --project src/MyApp.Infrastructure   --startup-project src/MyApp.Api   -o artifacts/sql/latest_idempotent.sql
```

---

## 9.3 Design-time factory

Se o `dotnet ef` não conseguir instanciar o contexto, implementar `IDesignTimeDbContextFactory<AppDbContext>`.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();

        optionsBuilder.UseSqlServer("Server=localhost;Database=appdb;Trusted_Connection=True;TrustServerCertificate=True");

        return new AppDbContext(optionsBuilder.Options);
    }
}
```

> Em produção, jamais deixar credenciais reais hardcoded aqui.

---

## 10) Estratégia de testes

## 10.1 Princípio

Para repositórios EF Core, **o principal valor está em testes de integração**, não em mocks excessivos de `DbSet`.

### Pirâmide recomendada

1. **Integração com o mesmo provider de produção** (preferencial)
   - ideal com Docker/Testcontainers
2. **Integração leve com SQLite em memória** (fallback útil)
3. **Unit tests** para regras puras fora do banco
4. **Evitar** depender de `EFCore.InMemory` como base principal

---

## 10.2 Casos mínimos obrigatórios

Cada repositório migrado deve ter testes cobrindo:

1. inserção com persistência correta
2. leitura por id
3. leitura com filtro
4. paginação
5. ordenação
6. projeção
7. carregamento de relacionamentos (`Include`) quando aplicável
8. update
9. delete
10. transação / rollback quando aplicável
11. índice/constraint relevante quando aplicável
12. concorrência quando aplicável

---

## 10.3 Testes com SQLite in-memory

```csharp
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

public static class TestDbContextFactory
{
    public static AppDbContext CreateSqliteInMemoryContext()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var context = new AppDbContext(options);
        context.Database.EnsureCreated();

        return context;
    }
}
```

### Observação

Manter a conexão aberta durante o teste para preservar o banco em memória.

---

## 11) Performance — regras obrigatórias

## 11.1 Leituras read-only

Usar `AsNoTracking()` em consultas somente leitura, especialmente em:
- listas
- buscas
- dashboards
- relatórios
- endpoints GET

---

## 11.2 Projetar ao invés de materializar tudo

Preferir:

```csharp
.Select(x => new Dto(...))
```

em vez de:
- carregar entidade completa
- depois mapear em memória
- principalmente em listagens

---

## 11.3 Evitar N+1

### Regras
- não usar lazy loading por padrão
- usar `Include/ThenInclude` quando precisar materializar relações
- ou usar projeção com `Select`
- revisar logs SQL em consultas críticas

---

## 11.4 Paginar sempre que a cardinalidade puder crescer

Toda listagem de produção deve:
- ter ordenação explícita
- usar `Skip/Take` ou cursor pagination quando aplicável
- evitar `ToListAsync()` sem limite em tabelas grandes

---

## 11.5 Índices (Avaliar a tabela também, se há indícies e se pode ser implementados de acordo com o Schema atual)

Criar índices coerentes com:
- filtros frequentes
- colunas de ordenação
- foreign keys compostas recorrentes
- unicidade de negócio

Exemplo:

```csharp
builder.HasIndex(x => x.Email).IsUnique();
builder.HasIndex(x => new { x.CustomerId, x.CreatedAtUtc });
```

---

## 11.6 Split queries

Quando houver risco de explosão cartesiana ao carregar muitas coleções relacionadas, avaliar split query.

Exemplo:

```csharp
var orders = await _dbContext.Orders
    .Include(x => x.Items)
    .AsSplitQuery()
    .ToListAsync(cancellationToken);
```

### Observação
Só usar após análise, pois split query pode aumentar roundtrips.

---

## 11.7 Hot paths

Para consultas extremamente frequentes e comprovadamente críticas:
- considerar compiled queries
- considerar context pooling
- considerar otimizações provider-specific

### Regra
Essas otimizações **não** entram na primeira onda da migração, a menos que exista benchmark justificando.

---

## 11.8 Benchmark antes de otimizar

O agente deve:
- medir endpoints/consultas críticas antes e depois
- comparar SQL gerado
- validar índice
- não supor que “ORM = lento” ou “LINQ = suficiente” sem evidência

---

## 12) Segurança — regras obrigatórias

## 12.1 Segredos

- não commitar connection strings com senha
- preferir:
  - environment variables

---

## 12.2 SQL Injection

Se houver SQL bruto:
- usar parâmetros
- preferir `FromSql` / `FromSqlInterpolated`
- evitar concatenação de string com input externo
- revisar todo ponto com `FromSqlRaw`

---

## 12.3 Logging sensível

- não habilitar `EnableSensitiveDataLogging()` em produção
- usar isso somente em ambiente de desenvolvimento/diagnóstico controlado (que ainda nao existe)

Exemplo seguro:

```csharp
builder.Services.AddDbContext<AppDbContext>((serviceProvider, options) =>
{
    var env = serviceProvider.GetRequiredService<IHostEnvironment>();
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

    options.UseSqlServer(connectionString);

    if (env.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
        options.EnableDetailedErrors();
    }
});
```

---

## 12.4 Menor privilégio operacional

O usuário de banco usado pela aplicação deve ter apenas as permissões necessárias para a aplicação.  
O usuário de deploy/migração pode ser distinto do usuário de runtime, quando a operação exigir mais privilégios.

---

## 12.5 Resiliência e erro

- não engolir exceções de persistência
- logar exceções relevantes com contexto
- tratar `DbUpdateException` e `DbUpdateConcurrencyException` adequadamente
- retornar erro de domínio/aplicação coerente

---

## 13) Critérios de aceite

A entrega só será considerada concluída quando todos os itens abaixo forem verdadeiros.

### Funcional
- [ ] Todos os repositórios do escopo foram migrados para EF Core
- [ ] O comportamento funcional anterior foi preservado
- [ ] Não existem queries diretas restantes no escopo, salvo exceções justificadas e documentadas
- [ ] Toda leitura e escrita crítica possui cobertura de teste

### Arquitetural
- [ ] Contratos públicos necessários foram preservados
- [ ] O `DbContext` está configurado via DI
- [ ] Mapeamentos estão centralizados e claros
- [ ] Não há lazy loading implícito por padrão
- [ ] Não há `IQueryable` vazando para camadas indevidas sem justificativa

### Banco (Somente as alterações que forem necessárias de fazer, criação de índices ou coisas do tipo)
- [ ] Existe pelo menos uma migration inicial ou incremental válida
- [ ] O banco sobe a partir das migrations
- [ ] Existe script SQL gerado para deploy
- [ ] Existe script idempotente para ambientes variáveis
- [ ] O pipeline ou processo valida pending model changes

### Qualidade
- [ ] Testes de integração passam
- [ ] Não há uso principal de `EFCore.InMemory` para simular comportamento relacional
- [ ] Há cobertura para filtros, paginação, ordenação, include/projeção e escrita

### Segurança
- [ ] Secrets não foram commitados
- [ ] Não há concatenação insegura em SQL bruto
- [ ] Sensitive logging está restrito a desenvolvimento

### Performance
- [ ] Leituras sem update usam `AsNoTracking()` quando apropriado
- [ ] Listagens usam projeção/paginação quando apropriado
- [ ] Consultas críticas foram revisadas quanto a N+1, índices e SQL gerado

---

## 14) Entregáveis esperados do agente

1. Código do `DbContext`
2. Configurações de entidades (`IEntityTypeConfiguration<>`)
3. Registro de DI
4. Repositórios migrados
5. Migrações geradas
6. Scripts SQL gerados
7. Testes de integração
8. Atualização de documentação do projeto
9. Remoção de código morto SQL legado do escopo migrado
10. Relatório final com:
   - o que foi migrado
   - exceções justificadas
   - riscos restantes
   - próximos passos

---

## 15) Checklist operacional do agente

## Antes de codar
- [ ] Inventariar repositórios e queries atuais
- [ ] Confirmar provider do banco
- [ ] Confirmar estrutura de projetos
- [ ] Criar baseline de testes dos fluxos críticos

## Durante a codificação
- [ ] Introduzir `DbContext`
- [ ] Mapear entidades com Fluent API
- [ ] Migrar um repositório por vez
- [ ] Validar tracking/no-tracking
- [ ] Revisar Includes e projeções
- [ ] Validar SQL gerado nos pontos críticos
- [ ] Criar migration

## Antes de concluir
- [ ] Rodar testes
- [ ] Gerar script SQL se necessário
- [ ] Gerar script idempotente se necessário
- [ ] Validar pending model changes
- [ ] Revisar secrets/logging
- [ ] Revisar código morto/remanescente

---

## 16) Regras de implementação para PRs

Toda PR da migração deve:

- ser pequena o suficiente para revisão
- migrar um conjunto coeso de repositórios/agregados
- incluir testes
- incluir migration quando houver mudança de schema
- incluir script/artifact quando aplicável
- documentar qualquer SQL bruto remanescente
- explicar trade-offs de performance quando houver

---

## 17) Prompt operacional sugerido para o agente de IA

Use este texto como instrução direta para o agente executor:

> Você está migrando a camada de acesso a dados de um projeto .NET 8 para Entity Framework Core. Preserve os contratos públicos dos repositórios já consumidos pela aplicação, mas substitua queries diretas por DbContext, DbSet, LINQ e Fluent API. Faça a migração incremental, um agregado por vez, com testes cobrindo leitura, escrita, paginação, ordenação, includes/projeções, transações e concorrência quando aplicável. Use EF Core 8.x e mantenha o provider na mesma major version. Gere migrations, script SQL revisável e script idempotente. Prefira integração com o provider real ou SQLite in-memory para testes, evitando depender de EF InMemory como base principal. Use AsNoTracking em leituras read-only, projeção para DTO em listagens, índices coerentes com filtros, e evite lazy loading por padrão. Não use FromSqlRaw com concatenação de input. Não habilite EnableSensitiveDataLogging em produção. Entregue código, testes, scripts, documentação e um resumo final dos pontos migrados e pendências justificadas.

---

## 18) Referências oficiais para o agente

## Visão geral
- EF Core overview: https://learn.microsoft.com/en-us/ef/core/
- Entity Framework hub: https://learn.microsoft.com/en-us/ef/

## DbContext e configuração
- DbContext lifetime/configuration: https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/
- DbContext API: https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.dbcontext

## Modelagem e mapeamento
- Creating and configuring a model: https://learn.microsoft.com/en-us/ef/core/modeling/
- Keys: https://learn.microsoft.com/en-us/ef/core/modeling/keys
- Indexes: https://learn.microsoft.com/en-us/ef/core/modeling/indexes

## Consultas
- Efficient querying: https://learn.microsoft.com/en-us/ef/core/performance/efficient-querying
- Tracking vs no-tracking: https://learn.microsoft.com/en-us/ef/core/querying/tracking
- Loading related data: https://learn.microsoft.com/en-us/ef/core/querying/related-data/
- SQL queries: https://learn.microsoft.com/en-us/ef/core/querying/sql-queries

## Saving / transações / concorrência
- Saving data: https://learn.microsoft.com/en-us/ef/core/saving/
- Transactions: https://learn.microsoft.com/en-us/ef/core/saving/transactions
- Concurrency: https://learn.microsoft.com/en-us/ef/core/saving/concurrency
- ExecuteUpdate / ExecuteDelete: https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete

## Performance
- Performance overview: https://learn.microsoft.com/en-us/ef/core/performance/
- Advanced performance topics: https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics

## Migrations e CLI
- Migrations overview: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/
- Applying migrations: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying
- Separate migrations project: https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/projects
- Design-time DbContext creation: https://learn.microsoft.com/en-us/ef/core/cli/dbcontext-creation
- EF Core CLI tools: https://learn.microsoft.com/en-us/ef/core/cli/dotnet

## Testes
- Testing overview: https://learn.microsoft.com/en-us/ef/core/testing/
- Choosing a testing strategy: https://learn.microsoft.com/en-us/ef/core/testing/choosing-a-testing-strategy
- Testing against production database: https://learn.microsoft.com/en-us/ef/core/testing/testing-with-the-database
- Testing without production database: https://learn.microsoft.com/en-us/ef/core/testing/testing-without-the-database
- ASP.NET Core integration tests: https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests

## Segurança e configuração
- Connection strings: https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-strings
- ASP.NET Core configuration: https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/
- Safe storage of app secrets: https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets
- Simple logging: https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/simple-logging

## Providers
- Database providers: https://learn.microsoft.com/en-us/ef/core/providers/
- Npgsql EF Core provider: https://www.npgsql.org/efcore/

---

## 19) Nota final para o agente

A migração será considerada boa **não** quando “usar EF Core em algum lugar”, e sim quando:

- a camada de persistência ficar mais simples e coerente
- a aplicação mantiver o mesmo comportamento
- o banco continuar evoluindo de forma segura por migrations
- testes aumentarem a confiança
- o acesso a dados ficar explícito, previsível e observável
- performance e segurança forem tratadas como requisitos desde a implementação
