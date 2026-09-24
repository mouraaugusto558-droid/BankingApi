# BankingApi — Mapa do Projeto (do início ao fim)

Este documento mostra a **ordem cronológica** em que o projeto foi construído: o que
existia em cada etapa, quais arquivos foram criados/alterados, por quê, e quais erros
apareceram no caminho. Serve para você conseguir "narrar" a evolução do projeto numa
entrevista, passo a passo, como se estivesse mostrando o histórico de commits.

> Para entender cada **conceito/tecnologia** em profundidade (o quê e por quê), veja
> [`APRENDIZADO.md`](./APRENDIZADO.md). Este arquivo aqui é sobre **ordem e evolução**.

---

## Etapa 0 — Esqueleto do projeto

Projeto criado com o template padrão de Web API do .NET (`dotnet new webapi`), com:

- `Program.cs` só com um endpoint mínimo de health-check:
  ```csharp
  var builder = WebApplication.CreateBuilder(args);
  var app = builder.Build();
  app.MapGet("/api/health", () => Results.Ok(new { status = "Healthy", ... }));
  app.Run();
  ```
- `BankingApi.csproj` (define o projeto e o framework alvo, `net8.0`).
- `bin/` e `obj/` — pastas geradas automaticamente pelo build, nunca editadas à mão.

**Estado ao final:** a API sobe e responde `GET /api/health`, mas não existe nenhum
model, controller ou banco de dados ainda.

---

## Etapa 1 — Model `Account`

**Objetivo:** representar uma conta bancária em código, sem tocar em banco de dados
ou endpoints ainda.

**Criado:** `Models/Account.cs`
```csharp
public class Account
{
    public int Id { get; set; }
    public string AccountNumber { get; set; } = string.Empty;
    public string HolderName { get; set; } = string.Empty;
    public decimal Balance { get; set; }
}
```

Um **model** aqui é só uma classe "molde de dados" — nenhuma lógica de banco, rede ou
regra de negócio ainda mora nela.

**Estado ao final:** o projeto compila com o novo model, mas nada o usa de fato (sem
endpoint, sem persistência).

---

## Etapa 2 — Primeiro endpoint (dados em memória)

**Objetivo:** ter uma API que realmente responde a requisições HTTP, antes de complicar
com banco de dados.

**Criado:** `Controllers/AccountController.cs`
```csharp
[ApiController]
[Route("api/accounts")]
public class AccountController : ControllerBase
{
    private static readonly List<Account> Accounts = new(); // "banco" temporário em RAM
    private static int _nextId = 1;

    public record CreateAccountRequest(string HolderName);

    [HttpPost]
    public ActionResult<Account> CreateAccount(CreateAccountRequest request)
    {
        var account = new Account
        {
            Id = _nextId++,
            AccountNumber = $"AC{DateTime.UtcNow.Ticks % 1_000_000}",
            HolderName = request.HolderName,
            Balance = 0
        };
        Accounts.Add(account);
        return Ok(account);
    }
}
```

**Alterado:** `Program.cs` — registrado o suporte a Controllers e Swagger:
```csharp
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
...
app.UseSwagger();
app.UseSwaggerUI();
app.MapControllers();
```

**Testado:** `POST /api/accounts` três vezes com o mesmo `HolderName` — confirmado que
geram 3 registros diferentes (o `Id` sempre incrementa e o `AccountNumber` varia pelo
timestamp; não existe nenhuma regra de unicidade de nome).

**Erro resolvido:** confusão com `http://0.0.0.0:5000` no terminal — esse é o endereço
em que o servidor *escuta* (todas as interfaces de rede), mas para acessar no navegador
é preciso usar `http://localhost:5000`.

**Estado ao final:** API funcional via Swagger, mas os dados **somem toda vez que a API
reinicia** — porque ficam só numa `List<Account>` em memória (`static`, dura enquanto o
processo roda).

---

## Etapa 3 — Persistência real (Docker + EF Core + SQL Server)

**Objetivo:** parar de perder os dados a cada restart — salvar de verdade num banco
relacional.

### 3.1 — Banco de dados via Docker

**Criado:** `docker-compose.yml` — descreve um container de SQL Server 2022, com porta
`1433` exposta e um volume nomeado (`sqlserver_data`) para os dados sobreviverem à
remoção do container.

### 3.2 — Pacotes NuGet

**Alterado:** `BankingApi.csproj` — adicionados:
- `Microsoft.EntityFrameworkCore.SqlServer` (driver do EF Core para SQL Server)
- `Microsoft.EntityFrameworkCore.Design` (necessário para gerar migrations)
- `Swashbuckle.AspNetCore` (Swagger)

### 3.3 — DbContext

**Criado:** `Data/BankingDbContext.cs` — a "sessão com o banco", com `DbSet<Account>`
e configuração via Fluent API (`OnModelCreating`): tipo `decimal(18,2)` para `Balance`,
índice único em `AccountNumber`.

### 3.4 — Connection string

**Alterado:** `appsettings.json` — adicionada a seção `ConnectionStrings:BankingDb`
apontando para `localhost,1433` (a porta exposta pelo Docker).

### 3.5 — Registro do DbContext

**Alterado:** `Program.cs`:
```csharp
builder.Services.AddDbContext<BankingDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("BankingDb")));
```

### 3.6 — Primeira migration

```bash
dotnet tool install --global dotnet-ef --version 8.0.11
dotnet ef migrations add InitialCreate
dotnet ef database update
```

Gerado `Migrations/{timestamp}_InitialCreate.cs` (cria a tabela `Accounts`) e aplicado
no banco — criando também a tabela de controle `__EFMigrationsHistory`.

### 3.7 — Controller reescrito para usar o banco

**Alterado:** `Controllers/AccountController.cs` — trocada a `List<Account>` estática
pelo `BankingDbContext` injetado via construtor. `CreateAccount` agora chama
`_context.Accounts.Add(...)` + `SaveChangesAsync()`. Adicionados:
- `GET /api/accounts` (lista todas)
- `GET /api/accounts/{id}` (busca uma, com `404` se não existir)

### Erros encontrados e resolvidos nesta etapa

| Erro | Causa | Solução |
|---|---|---|
| `NU1202` ao instalar o pacote SQL Server | `dotnet add package` puxou a versão mais nova (net10.0), incompatível com o projeto (net8.0) | Especificar `--version 8.0.11` |
| `dotnet-ef` não reconhecido | Ferramenta instalada mas não estava no `PATH` da sessão atual | `export PATH="$PATH:~/.dotnet/tools"` |
| Docker Desktop não conectava | Docker Desktop não estava aberto | Localizar o executável e iniciá-lo manualmente |
| `docker exec` com caminho Unix quebrado no Git Bash | Git Bash traduzia o path Unix para um path Windows | Prefixo `MSYS_NO_PATHCONV=1` |
| `CultureNotFoundException` ao rodar `dotnet ef database update` | `<InvariantGlobalization>true</InvariantGlobalization>` no `.csproj` é incompatível com o driver `Microsoft.Data.SqlClient` | Remover essa linha do `.csproj` |

**Testado (fluxo completo de persistência):** subir o Docker → rodar a API → criar
conta → listar/consultar → **reiniciar a API** → consultar de novo → confirmar que os
dados continuam lá.

**Estado ao final:** API com persistência real em SQL Server; ainda sem depósito,
saque, extrato ou autenticação (deixados propositalmente para depois).

---

## Etapa 4 — MVP completo (depósito, saque, saldo, extrato)

**Objetivo:** fechar o ciclo funcional de uma conta bancária básica.

### 4.1 — Nova entidade `Transaction`

**Criado:** `Models/Transaction.cs` — representa uma movimentação (`Deposit` ou
`Withdraw`), com `AccountId` (chave estrangeira), `Amount` e `CreatedAt`.

**Alterado:** `Models/Account.cs` — adicionada a coleção `ICollection<Transaction>
Transactions` (lado "1" do relacionamento 1-N).

### 4.2 — DbContext atualizado

**Alterado:** `Data/BankingDbContext.cs` — adicionado `DbSet<Transaction>` e, no
`OnModelCreating`, o relacionamento `Account` ↔ `Transaction` via Fluent API
(`HasOne().WithMany().HasForeignKey()`), com `OnDelete(DeleteBehavior.Cascade)`, e o
enum `TransactionType` configurado para ser salvo como texto no banco
(`HasConversion<string>()`).

### 4.3 — Segunda migration

```bash
dotnet ef migrations add AddTransactions
dotnet ef database update
```

Gerada e aplicada a migration que cria a tabela `Transactions` com a FK para `Accounts`
— o schema evoluiu **sem apagar** a tabela `Accounts` já existente, ilustrando bem o
propósito das migrations incrementais.

### 4.4 — Novos endpoints

**Alterado:** `Controllers/AccountController.cs` — adicionados:
- `POST /api/accounts/{id}/deposit` — soma ao saldo + registra a `Transaction`.
- `POST /api/accounts/{id}/withdraw` — subtrai do saldo, **bloqueando saldo negativo**
  (`400 Bad Request` se `Amount > Balance`), + registra a `Transaction`.
- `GET /api/accounts/{id}/balance` — retorna só o saldo atual.
- `GET /api/accounts/{id}/transactions` — retorna o extrato (mais recente primeiro).

Em depósito e saque, a atualização do `Balance` e a criação da `Transaction` são
enviadas juntas num único `SaveChangesAsync()` — o EF Core executa isso numa transação
SQL implícita, então ou os dois aplicam, ou nenhum aplica.

### 4.5 — Serialização de enum

**Alterado:** `Program.cs` — adicionado `JsonStringEnumConverter()` para que
`TransactionType` apareça como `"Deposit"`/`"Withdraw"` no JSON, em vez de `0`/`1`.

### Bug encontrado e resolvido nesta etapa

**Erro:** ao devolver uma `Account` com suas `Transactions` carregadas, o serializador
JSON entrava num ciclo infinito (`Account → Transactions → Account → ...`):
```
System.Text.Json.JsonException: A possible object cycle was detected...
```
**Causa:** `Transaction.Account` aponta de volta para a conta, que aponta de novo para
as transações.
**Solução:** `[JsonIgnore]` em `Transaction.Account` — o relacionamento continua
existindo no banco (via `AccountId`), só não é repetido na resposta JSON.

**Testado (fluxo completo do MVP):**
1. Criar conta → `POST /api/accounts`
2. Depositar → `POST /api/accounts/{id}/deposit`
3. Sacar → `POST /api/accounts/{id}/withdraw`
4. Tentar sacar mais que o saldo → `400 Bad Request`
5. Consultar saldo → `GET /api/accounts/{id}/balance`
6. Consultar extrato → `GET /api/accounts/{id}/transactions`
7. Chamar qualquer endpoint com um `id` inexistente → `404 Not Found`
8. **Reiniciar a API** → conferir que conta, saldo e extrato continuam salvos

**Estado ao final:** MVP funcional e persistente, documentado em `README.md` (como
rodar + exemplos de uso) e `APRENDIZADO.md` (conceitos em profundidade). Sem
autenticação, transferência entre contas ou testes automatizados — fora do escopo
combinado.

---

## Arquivo por arquivo (visão rápida)

| Arquivo | Criado na etapa | Responsabilidade |
|---|---|---|
| `Models/Account.cs` | 1 | Entidade Conta |
| `Controllers/AccountController.cs` | 2 (reescrito na 3, ampliado na 4) | Todos os endpoints HTTP |
| `Program.cs` | 0 (ampliado nas 2, 3 e 4) | Configuração da aplicação (DI, Swagger, pipeline HTTP) |
| `docker-compose.yml` | 3 | Sobe o SQL Server local |
| `Data/BankingDbContext.cs` | 3 (ampliado na 4) | Sessão com o banco + mapeamento das entidades |
| `appsettings.json` | 3 | Connection string do banco |
| `Migrations/*_InitialCreate.cs` | 3 | Cria a tabela `Accounts` |
| `Models/Transaction.cs` | 4 | Entidade Movimentação (depósito/saque) |
| `Migrations/*_AddTransactions.cs` | 4 | Cria a tabela `Transactions` |
| `README.md` | 4 | Como rodar o projeto + exemplos de endpoints |
| `APRENDIZADO.md` | 3 (ampliado na 4) | Explicação de cada conceito/tecnologia usada |
| `MAPA-DO-PROJETO.md` | 4 | Este arquivo — linha do tempo do projeto |

## Próximos passos possíveis (fora do escopo atual)

Se um dia quiser continuar evoluindo o projeto (não implementado de propósito):
autenticação/autorização (JWT), transferência entre contas, múltiplas moedas, testes
automatizados (unitários/integração), camada de serviços separada do controller.
