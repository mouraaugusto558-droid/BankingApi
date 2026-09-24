namespace BankingApi.Models;

// Model: representa uma conta bancária. Só dados, sem lógica de banco ou de rede.
public class Account
{
    public int Id { get; set; } // chave primária, gerada automaticamente pelo SQL Server
    public string AccountNumber { get; set; } = string.Empty; // número visível ao cliente, gerado no Controller
    public string HolderName { get; set; } = string.Empty;
    public decimal Balance { get; set; } // decimal (não float/double) evita erro de arredondamento em dinheiro

    // Lado "muitos" do relacionamento 1-N: uma conta tem várias movimentações.
    // Configurado via Fluent API em BankingDbContext.OnModelCreating.
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
}
