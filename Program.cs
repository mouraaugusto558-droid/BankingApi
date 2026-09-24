using System.Text.Json.Serialization;
using BankingApi.Data;
using Microsoft.EntityFrameworkCore;

// Ponto de entrada da aplicação (minimal hosting model do .NET 8).
// Aqui é onde os serviços são registrados no container de Injeção de Dependência (DI)
// e o pipeline HTTP é configurado, antes da API começar a escutar requisições.
var builder = WebApplication.CreateBuilder(args);

// Habilita Controllers (classes [ApiController]) e configura o enum TransactionType
// para serializar como texto ("Deposit"/"Withdraw") em vez de número (0/1) no JSON.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Geram a documentação/interface do Swagger (só ativada em Development, mais abaixo).
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Registra o DbContext no container de DI com tempo de vida Scoped (uma instância por
// requisição HTTP). A connection string vem de appsettings.json -> ConnectionStrings:BankingDb.
builder.Services.AddDbContext<BankingDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("BankingDb")));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(); // interface web em /swagger para testar os endpoints manualmente
}

app.MapControllers(); // ativa as rotas definidas nos [ApiController] (ex.: AccountController)

// Endpoint simples (minimal API, sem Controller) só para checar se a API está no ar.
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "Healthy",
    message = "Banking API está funcionando!",
    timestamp = DateTime.UtcNow
}));

app.Run();
