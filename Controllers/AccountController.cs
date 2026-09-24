using BankingApi.Data;
using BankingApi.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BankingApi.Controllers;

// Controller: recebe requisições HTTP em "api/accounts/..." e devolve respostas.
// [ApiController] ativa validação/model binding automáticos de API.
[ApiController]
[Route("api/accounts")]
public class AccountController : ControllerBase
{
    private readonly BankingDbContext _context;

    // BankingDbContext não é criado manualmente aqui: é injetado pelo framework
    // (Dependency Injection), porque foi registrado em Program.cs com AddDbContext<>().
    public AccountController(BankingDbContext context)
    {
        _context = context;
    }

    public record CreateAccountRequest(string HolderName);

    // POST /api/accounts -> cria uma conta nova.
    [HttpPost]
    public async Task<ActionResult<Account>> CreateAccount(CreateAccountRequest request)
    {
        var account = new Account
        {
            AccountNumber = $"AC{DateTime.UtcNow.Ticks % 1_000_000}", // número simples, só para fins didáticos
            HolderName = request.HolderName,
            Balance = 0
        };

        _context.Accounts.Add(account); // marca como "Added" em memória; nada é salvo ainda
        await _context.SaveChangesAsync(); // aqui o INSERT realmente roda no SQL Server

        // 201 Created + header Location apontando para GET /api/accounts/{id}
        return CreatedAtAction(nameof(GetAccountById), new { id = account.Id }, account);
    }

    // GET /api/accounts -> lista todas as contas.
    [HttpGet]
    public async Task<ActionResult<List<Account>>> GetAccounts()
    {
        var accounts = await _context.Accounts.ToListAsync();
        return Ok(accounts);
    }

    // GET /api/accounts/{id} -> busca uma conta específica; 404 se não existir.
    [HttpGet("{id:int}")]
    public async Task<ActionResult<Account>> GetAccountById(int id)
    {
        var account = await _context.Accounts.FindAsync(id);

        if (account is null)
        {
            return NotFound(new { message = $"Conta com Id {id} não encontrada." });
        }

        return Ok(account);
    }

    public record TransactionRequest(decimal Amount);

    // POST /api/accounts/{id}/deposit -> soma valor ao saldo e registra a movimentação.
    [HttpPost("{id:int}/deposit")]
    public async Task<ActionResult<Account>> Deposit(int id, TransactionRequest request)
    {
        var account = await _context.Accounts.FindAsync(id);

        if (account is null)
        {
            return NotFound(new { message = $"Conta com Id {id} não encontrada." });
        }

        if (request.Amount <= 0)
        {
            return BadRequest(new { message = "O valor do depósito deve ser maior que zero." });
        }

        account.Balance += request.Amount;

        _context.Transactions.Add(new Transaction
        {
            AccountId = account.Id,
            Type = TransactionType.Deposit,
            Amount = request.Amount
        });

        // Balance (UPDATE) e a nova Transaction (INSERT) ficam pendentes no change tracker
        // e são enviados juntos em UMA transação SQL implícita aqui: ou os dois aplicam, ou nenhum.
        await _context.SaveChangesAsync();

        return Ok(account);
    }

    // POST /api/accounts/{id}/withdraw -> subtrai valor do saldo, bloqueando saldo negativo.
    [HttpPost("{id:int}/withdraw")]
    public async Task<ActionResult<Account>> Withdraw(int id, TransactionRequest request)
    {
        var account = await _context.Accounts.FindAsync(id);

        if (account is null)
        {
            return NotFound(new { message = $"Conta com Id {id} não encontrada." });
        }

        if (request.Amount <= 0)
        {
            return BadRequest(new { message = "O valor do saque deve ser maior que zero." });
        }

        // Regra de negócio central do saque: valida ANTES de tocar no saldo e ANTES de salvar.
        if (account.Balance < request.Amount)
        {
            return BadRequest(new { message = "Saldo insuficiente para realizar o saque." });
        }

        account.Balance -= request.Amount;

        _context.Transactions.Add(new Transaction
        {
            AccountId = account.Id,
            Type = TransactionType.Withdraw,
            Amount = request.Amount
        });

        await _context.SaveChangesAsync();

        return Ok(account);
    }

    // GET /api/accounts/{id}/balance -> saldo atual, sem trazer o histórico inteiro.
    [HttpGet("{id:int}/balance")]
    public async Task<ActionResult> GetBalance(int id)
    {
        var account = await _context.Accounts.FindAsync(id);

        if (account is null)
        {
            return NotFound(new { message = $"Conta com Id {id} não encontrada." });
        }

        return Ok(new { accountId = account.Id, balance = account.Balance });
    }

    // GET /api/accounts/{id}/transactions -> extrato (todas as movimentações da conta).
    [HttpGet("{id:int}/transactions")]
    public async Task<ActionResult<List<Transaction>>> GetStatement(int id)
    {
        // AnyAsync só verifica existência (SELECT 1 ... WHERE ...), mais barato que carregar a conta inteira.
        var accountExists = await _context.Accounts.AnyAsync(a => a.Id == id);

        if (!accountExists)
        {
            return NotFound(new { message = $"Conta com Id {id} não encontrada." });
        }

        var transactions = await _context.Transactions
            .Where(t => t.AccountId == id)
            .OrderByDescending(t => t.CreatedAt) // mais recente primeiro
            .ToListAsync();

        return Ok(transactions);
    }
}
