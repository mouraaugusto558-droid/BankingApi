using System.Text.Json.Serialization;

namespace BankingApi.Models;

public enum TransactionType
{
    Deposit,
    Withdraw
}

// Model: registra uma movimentação (depósito ou saque) de uma conta.
// É o histórico que forma o extrato — nunca é editado ou apagado depois de criado.
public class Transaction
{
    public int Id { get; set; }
    public int AccountId { get; set; } // chave estrangeira para Account

    // [JsonIgnore] evita um ciclo infinito ao serializar para JSON:
    // Account -> Transactions -> Account -> Transactions -> ...
    // O relacionamento continua existindo no banco via AccountId; só não é repetido na resposta.
    [JsonIgnore]
    public Account? Account { get; set; }

    public TransactionType Type { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; // usado para ordenar o extrato do mais recente pro mais antigo
}
