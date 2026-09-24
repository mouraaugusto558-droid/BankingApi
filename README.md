# BankingApi

API bancária simples em **ASP.NET Core (.NET 8)**, com persistência em **SQL Server**
via **Entity Framework Core**, banco de dados rodando em **Docker**.

Projeto de aprendizado — cobre criação de contas, depósito, saque, extrato e saldo,
com dados realmente persistidos (sobrevivem a reinícios da API).

Para uma explicação didática de cada conceito usado (C#, ASP.NET Core, Docker, EF Core,
Migrations etc.), veja [`APRENDIZADO.md`](./APRENDIZADO.md).

---

## Pré-requisitos

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (para o SQL Server)

## Como executar

**1. Subir o SQL Server via Docker**

```bash
docker compose up -d
```

**2. Aplicar as migrations (cria o schema do banco)**

```bash
dotnet tool install --global dotnet-ef --version 8.0.11   # só na primeira vez
dotnet ef database update
```

**3. Rodar a API**

```bash
dotnet run
```

A API sobe em `http://localhost:5000`. O Swagger (interface para testar os endpoints
no navegador) fica em `http://localhost:5000/swagger`.

**4. Verificar que está no ar**

```bash
curl http://localhost:5000/api/health
```

---

## Endpoints

| Verbo | Rota | Descrição |
|---|---|---|
| `POST` | `/api/accounts` | Cria uma conta |
| `GET` | `/api/accounts` | Lista todas as contas |
| `GET` | `/api/accounts/{id}` | Busca uma conta pelo Id |
| `POST` | `/api/accounts/{id}/deposit` | Deposita um valor na conta |
| `POST` | `/api/accounts/{id}/withdraw` | Saca um valor da conta (bloqueia saldo negativo) |
| `GET` | `/api/accounts/{id}/balance` | Retorna o saldo atual |
| `GET` | `/api/accounts/{id}/transactions` | Retorna o extrato (histórico de movimentações) |

### Criar conta

```bash
curl -X POST http://localhost:5000/api/accounts \
  -H "Content-Type: application/json" \
  -d '{"holderName":"Douglas Costa"}'
```

```json
{
  "id": 1,
  "accountNumber": "AC761245",
  "holderName": "Douglas Costa",
  "balance": 0,
  "transactions": []
}
```

### Depositar

```bash
curl -X POST http://localhost:5000/api/accounts/1/deposit \
  -H "Content-Type: application/json" \
  -d '{"amount":100}'
```

### Sacar

```bash
curl -X POST http://localhost:5000/api/accounts/1/withdraw \
  -H "Content-Type: application/json" \
  -d '{"amount":30}'
```

Se o valor solicitado for maior que o saldo, a API responde `400 Bad Request`:

```json
{ "message": "Saldo insuficiente para realizar o saque." }
```

### Consultar saldo

```bash
curl http://localhost:5000/api/accounts/1/balance
```

```json
{ "accountId": 1, "balance": 70.00 }
```

### Consultar extrato

```bash
curl http://localhost:5000/api/accounts/1/transactions
```

```json
[
  { "id": 2, "accountId": 1, "type": "Withdraw", "amount": 30.00, "createdAt": "..." },
  { "id": 1, "accountId": 1, "type": "Deposit", "amount": 100.00, "createdAt": "..." }
]
```

### Conta inexistente

Qualquer endpoint que recebe `{id}` retorna `404 Not Found` se a conta não existir:

```json
{ "message": "Conta com Id 999 não encontrada." }
```

---

## Estrutura do projeto

```
BankingApi/
├── Controllers/
│   └── AccountController.cs      # todos os endpoints
├── Models/
│   ├── Account.cs                # entidade Conta
│   └── Transaction.cs            # entidade Transação (depósito/saque)
├── Data/
│   └── BankingDbContext.cs       # DbContext do EF Core
├── Migrations/                   # histórico de mudanças de schema
├── Program.cs                    # configuração da aplicação (DI, Swagger, etc.)
├── appsettings.json               # connection string
├── docker-compose.yml            # sobe o SQL Server local
├── README.md                     # este arquivo
└── APRENDIZADO.md                # explicação didática dos conceitos usados
```

## Escopo (o que este projeto NÃO tem)

Propositalmente, para manter o projeto pequeno e didático, não há: autenticação,
autorização, múltiplas moedas, transferência entre contas, testes automatizados ou
arquitetura em camadas (services/repositories separados). O `AccountController` fala
diretamente com o `DbContext`.
