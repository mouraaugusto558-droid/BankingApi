using BankingApi.Models;
using Microsoft.EntityFrameworkCore;

namespace BankingApi.Data;

// DbContext = uma "sessão" com o banco de dados. Sabe qual conexão usar, rastreia
// mudanças nos objetos carregados (change tracking) e gera o SQL ao chamar SaveChangesAsync().
// Registrado no Program.cs com AddDbContext<BankingDbContext>() e injetado nos controllers.
public class BankingDbContext : DbContext
{
    public BankingDbContext(DbContextOptions<BankingDbContext> options) : base(options)
    {
    }

    // Cada DbSet<T> representa uma tabela, consultável via LINQ (funciona como um Repository).
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Transaction> Transactions => Set<Transaction>();

    // Fluent API: configura o mapeamento classe -> tabela sem precisar de atributos no model.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(entity =>
        {
            entity.HasKey(a => a.Id);
            entity.Property(a => a.AccountNumber).IsRequired().HasMaxLength(20);
            entity.Property(a => a.HolderName).IsRequired().HasMaxLength(200);
            entity.Property(a => a.Balance).HasColumnType("decimal(18,2)"); // evita imprecisão numérica em valores monetários
            entity.HasIndex(a => a.AccountNumber).IsUnique(); // impede duas contas com o mesmo número
        });

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Amount).HasColumnType("decimal(18,2)");
            entity.Property(t => t.Type).HasConversion<string>().HasMaxLength(20); // salva o enum como texto ("Deposit"), não como número
            entity.Property(t => t.CreatedAt).IsRequired();

            // Relacionamento 1-N: uma Account tem várias Transactions, ligadas por AccountId.
            entity.HasOne(t => t.Account)
                  .WithMany(a => a.Transactions)
                  .HasForeignKey(t => t.AccountId)
                  .OnDelete(DeleteBehavior.Cascade); // se a conta for apagada, suas transações somem junto (evita registro órfão)
        });
    }
}
