# BankingApi — Documento de Aprendizado

Este documento resume os conceitos aprendidos construindo a BankingApi, uma API bancária
em **ASP.NET Core (.NET 8)** com persistência em **SQL Server** via **Entity Framework
Core**, rodando o banco em **Docker**.

Objetivo: servir como material de revisão — inclusive para entrevistas técnicas.

---

## 1. Estrutura do projeto

```
BankingApi/
├── Controllers/
│   └── AccountController.cs
├── Models/
│   ├── Account.cs
│   └── Transaction.cs
├── Data/
│   └── BankingDbContext.cs
├── Migrations/
│   ├── {timestamp}_InitialCreate.cs
│   ├── {timestamp}_AddTransactions.cs
│   └── BankingDbContextModelSnapshot.cs
├── Program.cs
├── appsettings.json
├── appsettings.Development.json
├── docker-compose.yml
├── BankingApi.csproj
├── bin/          ← gerado pelo build (não versionar)
└── obj/          ← gerado pelo build (não versionar)
```

- **`bin/` e `obj/`** são artefatos de compilação, recriados a cada `dotnet build`/`dotnet run`.
  `bin/` guarda o `.dll` final; `obj/` guarda arquivos intermediários de build. Nunca são
  editados manualmente e devem estar no `.gitignore`.

---

## 2. Linguagem e stack

- **C#** — única linguagem de código-fonte do projeto.
- **ASP.NET Core (.NET 8)** — framework web usado para criar a API.
- **Entity Framework Core (EF Core)** — ORM usado para persistência.
- **SQL Server** — banco de dados relacional, rodando em container Docker.
- **Swagger (Swashbuckle)** — interface web para testar os endpoints manualmente.

---

## 3. Models — `Account` e `Transaction`

```csharp
public class Account
{
    public int Id { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public string HolderName { get; set; } = string.Empty;
    public decimal Balance { get; set; }
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}

public enum TransactionType { Deposit, Withdraw }

public class Transaction
{
    public int Id { get; set; }
    public int AccountId { get; set; }

    [JsonIgnore]
    public Account? Account { get; set; }

    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

Um **model** é uma classe que representa um conceito do domínio de negócio. Não tem
lógica de banco de dados nem de rede; é só um "molde de dados".

| Propriedade | Tipo | Significado |
|---|---|---|
| `Account.Id` | `int` | Identificador técnico interno (chave primária). |
| `Account.AccountNumber` | `string` | Número da conta visível ao cliente, diferente do `Id` interno. |
| `Account.Balance` | `decimal` | Saldo. Usa `decimal` (não `float`/`double`) para evitar erro de arredondamento em dinheiro. |
| `Transaction.AccountId` | `int` | Chave estrangeira — a qual conta pertence essa movimentação. |
| `Transaction.Type` | `enum` | `Deposit` ou `Withdraw`. |
| `Transaction.CreatedAt` | `DateTime` | Quando a movimentação ocorreu — base do extrato. |

### Relacionamento entre as entidades

`Account` tem muitas `Transaction` (1 conta → N movimentações). No banco, isso vira uma
**chave estrangeira** `Transactions.AccountId → Accounts.Id`.

### Por que `[JsonIgnore]` em `Transaction.Account`

Sem essa anotação, serializar uma `Transaction` para JSON tentaria também serializar sua
`Account`, que por sua vez tem uma lista de `Transaction`, que aponta de volta pra
`Account`... um **ciclo infinito** (`Account → Transactions → Account → Transactions → ...`).
`[JsonIgnore]` corta esse ciclo, mantendo a navegação `AccountId` (só o número, sem o objeto).

---

## 4. Controller e conceitos de API — `Controllers/AccountController.cs`

```csharp
[ApiController]
[Route("api/accounts")]
public class AccountController : ControllerBase
{
    private readonly BankingDbContext _context;

    public AccountController(BankingDbContext context) => _context = context;

    [HttpPost]                          public async Task<ActionResult<Account>> CreateAccount(...) { ... }
    [HttpGet]                           public async Task<ActionResult<List<Account>>> GetAccounts() { ... }
    [HttpGet("{id:int}")]               public async Task<ActionResult<Account>> GetAccountById(int id) { ... }
    [HttpPost("{id:int}/deposit")]      public async Task<ActionResult<Account>> Deposit(int id, ...) { ... }
    [HttpPost("{id:int}/withdraw")]     public async Task<ActionResult<Account>> Withdraw(int id, ...) { ... }
    [HttpGet("{id:int}/balance")]       public async Task<ActionResult> GetBalance(int id) { ... }
    [HttpGet("{id:int}/transactions")]  public async Task<ActionResult<List<Transaction>>> GetStatement(int id) { ... }
}
```

### Conceitos

- **Controller** — classe responsável por receber requisições HTTP e devolver respostas.
  `[ApiController]` ativa comportamentos automáticos de API (validação, binding de JSON).
  `[Route("api/accounts")]` define o prefixo de URL de todas as rotas da classe.

- **Endpoint** — combinação específica de **URL + verbo HTTP** exposta pela API.
  Ex.: `POST /api/accounts/1/deposit` e `GET /api/accounts/1/balance` são endpoints
  diferentes, mesmo compartilhando o `1`.

- **Rota aninhada** (`{id:int}/deposit`) — o `{id}` da URL identifica a conta-alvo da
  operação; o `:int` é uma *route constraint* (só casa se for número).

- **Verbos HTTP**:
  - `GET` → buscar dados, sem alterar nada (idempotente, sem efeito colateral).
  - `POST` → criar um recurso ou disparar uma ação que muda estado (aqui: depósito/saque).
  - `PUT`/`PATCH` → atualizar um recurso existente (não usado neste projeto).
  - `DELETE` → remover um recurso (não usado neste projeto).

- **Request/Response** — o cliente envia um JSON (ex.: `{"amount": 100}`), o ASP.NET Core
  converte automaticamente para `TransactionRequest request` (*model binding*); a API
  devolve JSON + um **status HTTP**:
  - `200 OK` — sucesso.
  - `201 Created` — recurso criado (`POST /api/accounts`, via `CreatedAtAction`).
  - `400 Bad Request` — requisição inválida (valor ≤ 0, saldo insuficiente).
  - `404 Not Found` — conta não existe.

### Regra de negócio: saque sem saldo negativo

```csharp
if (account.Balance < request.Amount)
{
    return BadRequest(new { message = "Saldo insuficiente para realizar o saque." });
}
```

A validação acontece **antes** de alterar `account.Balance` e **antes** de chamar
`SaveChangesAsync()` — nada é persistido se a regra falhar.

### Por que depósito/saque e a transação são salvos juntos

```csharp
account.Balance += request.Amount;
_context.Transactions.Add(new Transaction { ... });
await _context.SaveChangesAsync();
```

As duas mudanças (atualizar `Balance` e inserir a `Transaction`) ficam pendentes em
memória e são enviadas ao banco em uma única chamada a `SaveChangesAsync()`. O EF Core
executa isso dentro de **uma transação SQL implícita**: ou os dois `UPDATE`/`INSERT`
funcionam, ou nenhum é aplicado — evitando um estado inconsistente (saldo alterado sem
o registro da movimentação, ou vice-versa).

### Injeção de Dependência (DI)

```csharp
public AccountController(BankingDbContext context) => _context = context;
```

O `BankingDbContext` não é criado manualmente dentro do controller — ele é **injetado**
pelo framework via construtor, porque foi registrado em `Program.cs` com
`AddDbContext<BankingDbContext>(...)`. Padrão **Dependency Injection**, nativo do
ASP.NET Core.

---

## 5. Docker e `docker-compose.yml`

```yaml
services:
  sqlserver:
    image: mcr.microsoft.com/mssql/server:2022-latest
    container_name: bankingapi-sqlserver
    environment:
      ACCEPT_EULA: "Y"
      MSSQL_SA_PASSWORD: "yourStrong(!)Password"
    ports:
      - "1433:1433"
    volumes:
      - sqlserver_data:/var/opt/mssql

volumes:
  sqlserver_data:
```

### Conceitos

- **Docker** empacota uma aplicação com tudo que precisa para rodar (imagem) e a executa
  em um processo isolado (container), compartilhando o kernel do SO host — diferente de
  uma VM, que virtualiza o hardware inteiro. Por isso containers são mais leves e rápidos.

- **`docker-compose`** descreve e orquestra múltiplos containers (e redes/volumes) em um
  único arquivo YAML, evitando comandos `docker run` longos e manuais.

### Linha por linha

| Chave | Significado |
|---|---|
| `image` | Imagem base usada para criar o container (SQL Server 2022 oficial). |
| `container_name` | Nome fixo do container. |
| `environment` | Variáveis de ambiente passadas para dentro do container (`ACCEPT_EULA`, senha do `sa`). |
| `ports: "1433:1433"` | Mapeamento `porta_host:porta_container`, expõe a porta do SQL Server para fora do container. |
| `volumes` | Monta uma pasta persistente gerenciada pelo Docker dentro do container, para os dados sobreviverem à remoção do container. |

**Ponto-chave:** containers são efêmeros por padrão — sem `volumes`, os dados do banco
seriam perdidos ao remover o container. O volume nomeado `sqlserver_data` resolve isso.

### Perguntas de entrevista
- Diferença entre container e VM → kernel compartilhado (leve) vs. hardware virtualizado (pesado, mais isolado).
- Por que Docker para banco de dados local → ambiente reprodutível, fácil de destruir/recriar, não "suja" o SO.
- O que acontece com os dados em `docker compose down` → container é removido, mas o volume nomeado permanece (a menos que use `-v`).

---

## 6. Entity Framework Core e o `DbContext`

```csharp
public class BankingDbContext : DbContext
{
    public BankingDbContext(DbContextOptions<BankingDbContext> options) : base(options) { }

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.AccountNumber).IsRequired().HasMaxLength(20);
            entity.Property(a => a.HolderName).IsRequired().HasMaxLength(200);
            entity.Property(a => a.Balance).HasColumnType("decimal(18,2)");
            entity.HasIndex(a => a.AccountNumber).IsUnique();
        });

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Amount).HasColumnType("decimal(18,2)");
            entity.Property(t => t.Type).HasConversion<string>().HasMaxLength(20);

            entity.HasOne(t => t.Account)
                  .WithMany(a => a.Transactions)
                  .HasForeignKey(t => t.AccountId)
                  .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
```

### O que é EF Core
Um **ORM** (Object-Relational Mapper): traduz entre objetos C# e tabelas relacionais,
evitando SQL manual repetitivo.

### O que é o `DbContext`
Representa uma **sessão com o banco de dados**. Sabe qual conexão usar, rastreia
mudanças nos objetos carregados (*change tracking*) e gera o SQL necessário quando
`SaveChangesAsync()` é chamado.

Padrões envolvidos:
- **Unit of Work** — acumula mudanças em memória e só as envia ao banco (numa transação)
  quando `SaveChanges()` é chamado.
- **Repository** (parecido) — `DbSet<T>` é a "tabela" consultável via LINQ.

```csharp
_context.Accounts.FindAsync(id);        // SELECT * FROM Accounts WHERE Id = @id
_context.Transactions.Add(transaction); // marca como Added; só executa no SaveChanges
_context.Transactions
    .Where(t => t.AccountId == id)
    .OrderByDescending(t => t.CreatedAt)
    .ToListAsync();                     // SELECT ... WHERE AccountId = @id ORDER BY CreatedAt DESC
```

### `OnModelCreating` (Fluent API)
Configura detalhes do mapeamento classe → tabela sem usar atributos na classe
(alternativa às *data annotations*):
- `HasColumnType("decimal(18,2)")` — evita imprecisão numérica em valores monetários.
- `HasIndex(...).IsUnique()` — impede duas contas com o mesmo `AccountNumber`.
- `HasConversion<string>()` — grava o enum `TransactionType` como texto (`"Deposit"`)
  no banco em vez de número, deixando as linhas legíveis direto no SQL Server.
- `HasOne(...).WithMany(...).HasForeignKey(...)` — declara o relacionamento
  1-para-N entre `Account` e `Transaction` (chave estrangeira `AccountId`).
- `OnDelete(DeleteBehavior.Cascade)` — se uma conta for apagada, suas transações são
  apagadas junto (evita registros órfãos).

### Registro no `Program.cs`

```csharp
builder.Services.AddDbContext<BankingDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("BankingDb")));
```

Registra o `DbContext` no container de **injeção de dependência**, com tempo de vida
padrão **Scoped** (uma instância nova por requisição HTTP).

⚠️ **`DbContext` não é thread-safe** — não pode ser compartilhado como Singleton
entre requisições simultâneas.

### Perguntas de entrevista
- O que é um ORM e por que usar → menos SQL manual, mais produtividade, porém pode gerar queries menos otimizadas que SQL escrito à mão.
- Tempo de vida padrão do `DbContext` no DI → Scoped.
- `DbContext` é thread-safe? → Não.
- O que é change tracking? → o DbContext compara o estado atual dos objetos com o original para gerar só o `UPDATE` necessário.
- Como o EF Core garante que saldo e transação sejam salvos juntos? → ambos ficam pendentes no *change tracker* e são enviados numa única transação SQL implícita dentro de `SaveChangesAsync()`.
- Diferença entre `Add` e `SaveChanges` → `Add` só marca em memória; o SQL só executa em `SaveChanges()`.

---

## 7. Connection String

```json
"ConnectionStrings": {
  "BankingDb": "Server=localhost,1433;Database=BankingApiDb;User Id=sa;Password=yourStrong(!)Password;TrustServerCertificate=True"
}
```

Diz ao driver do banco **como e onde conectar**:

| Parte | Significado |
|---|---|
| `Server=localhost,1433` | Endereço e porta do SQL Server (porta exposta pelo `docker-compose.yml`). |
| `Database=BankingApiDb` | Nome do banco específico dentro do servidor. |
| `User Id=sa` / `Password=...` | Credenciais (autenticação SQL — login/senha do próprio SQL Server). |
| `TrustServerCertificate=True` | Confia no certificado autoassinado do container sem validar a cadeia — aceitável só em dev local. |

### Como é usada
```csharp
builder.Configuration.GetConnectionString("BankingDb")
```
O ASP.NET Core lê configuração em camadas: `appsettings.json` → `appsettings.{Environment}.json`
→ variáveis de ambiente → user-secrets/args — cada camada posterior sobrescreve a anterior.

### ⚠️ Segurança
Senha em texto puro no `appsettings.json` é aceitável só em desenvolvimento local.
Em produção, usar:
- `dotnet user-secrets` (segredos fora do repositório, só em dev)
- Variáveis de ambiente (comum em containers/produção)
- Secret managers (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault)

### Perguntas de entrevista
- Como proteger uma connection string em produção → nunca commitada; usar secret manager/variável de ambiente.
- O que acontece se servidor/porta estiverem errados → `SqlException` ao abrir a conexão, geralmente na primeira query (o `AddDbContext` é "lazy").
- Autenticação SQL vs. Windows/integrada → SQL usa usuário/senha no próprio banco; Windows usa o token do usuário do domínio/SO (`Integrated Security=True`).

---

## 8. Migrations

```bash
dotnet ef migrations add InitialCreate
dotnet ef migrations add AddTransactions
dotnet ef database update
```

### O problema que resolve
Mantém o **schema do banco de dados** sincronizado com os **models em C#**, de forma
versionada e reproduzível — como um "git para o schema do banco".

### O que cada comando faz

**`dotnet ef migrations add <Nome>`**
1. Compara o modelo atual com o último snapshot conhecido (`Migrations/BankingDbContextModelSnapshot.cs`).
2. Gera um arquivo (`Migrations/{timestamp}_<Nome>.cs`) com:
   - `Up()` — como aplicar a mudança (ex.: `CREATE TABLE Transactions (...)`, com a FK para `Accounts`).
   - `Down()` — como desfazer (`DROP TABLE Transactions`), usado em rollback.
3. Nada é enviado ao banco ainda — é só código C# gerado.

**`dotnet ef database update`**
1. Conecta no banco real.
2. Consulta a tabela de controle `__EFMigrationsHistory` (lista as migrations já aplicadas).
3. Executa, em ordem, o `Up()` de cada migration pendente (aqui: `InitialCreate`, depois `AddTransactions`).
4. Registra cada migration como aplicada em `__EFMigrationsHistory`.

Como o projeto evoluiu em duas migrations (`InitialCreate` cria `Accounts`;
`AddTransactions` cria `Transactions` com a FK para `Accounts`), isso ilustra bem como o
schema cresce **incrementalmente**, sem apagar o banco a cada mudança.

### Por que importa em produção/equipe
- Todo desenvolvedor aplica exatamente as mesmas mudanças, na mesma ordem, via git.
- Times grandes costumam gerar o SQL com `dotnet ef migrations script` e revisar/aplicar
  manualmente em produção, em vez de rodar `database update` direto.
- Permite rollback para uma migration anterior.

### Perguntas de entrevista
- O que é uma migration no EF Core → registro versionado de mudanças de schema, gerado comparando o modelo atual com o snapshot anterior.
- Como reverter uma migration → `dotnet ef database update NomeAnterior` (roda `Down()`) ou `dotnet ef migrations remove` (remove a última, se ainda não aplicada).
- O que é `__EFMigrationsHistory` → tabela de controle interna que registra quais migrations já rodaram naquele banco.
- Migrations são sempre seguras em produção? → Não necessariamente — adicionar coluna nullable é seguro; renomear/remover coluna ou adicionar `NOT NULL` sem default numa tabela com dados pode quebrar. Por isso se revisa o SQL gerado antes de aplicar.
- Alternativa a Code-First → Database-First, gerando classes C# a partir de um banco já existente (`dotnet ef dbcontext scaffold`), útil para bancos legados.

---

## 9. Armadilhas resolvidas

### `InvariantGlobalization`
O `.csproj` originalmente tinha:
```xml
<InvariantGlobalization>true</InvariantGlobalization>
```
Essa flag reduz o tamanho do runtime removendo dados de globalização/cultura — mas
quebra o driver `Microsoft.Data.SqlClient`, que precisa dessas informações para abrir
conexão com o SQL Server (`CultureNotFoundException`). Foi removida para permitir a
conexão com o banco.

### Ciclo de serialização JSON entre `Account` e `Transaction`
Ao carregar `account.Transactions` e devolver a `Account` como resposta, o serializador
JSON tentava percorrer `Account → Transactions → Account → Transactions → ...`
infinitamente (`System.Text.Json.JsonException: A possible object cycle was detected`).
Resolvido marcando a navegação de volta com `[JsonIgnore]` em `Transaction.Account` —
o relacionamento continua existindo no banco (via `AccountId`), só não é serializado
duas vezes no JSON de resposta.

---

## 10. Checklist do fluxo completo testado

- [x] Subir o SQL Server → `docker compose up -d`
- [x] Iniciar a API → `dotnet run`
- [x] Cadastrar uma conta → `POST /api/accounts`
- [x] Consultar as contas → `GET /api/accounts` e `GET /api/accounts/{id}`
- [x] Depositar → `POST /api/accounts/{id}/deposit`
- [x] Sacar → `POST /api/accounts/{id}/withdraw`
- [x] Bloquear saque acima do saldo disponível → `400 Bad Request`
- [x] Consultar saldo atual → `GET /api/accounts/{id}/balance`
- [x] Consultar extrato → `GET /api/accounts/{id}/transactions`
- [x] Retornar `404` para conta inexistente em qualquer endpoint com `{id}`
- [x] Reiniciar a API e confirmar que contas e transações continuam no banco (persistência real)

Ainda **não implementados** (propositalmente, fora do escopo deste MVP): autenticação,
transferência entre contas, múltiplas moedas, testes automatizados.
