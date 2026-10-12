using gRPC_server.Broker;
using gRPC_server.Data;
using gRPC_server.Models;
using gRPC_server.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Base de dados (SQL Server via Entity Framework Core).
// A connection string pode ser substituída pela variável de ambiente ConnectionStrings__DefaultConnection.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddDbContext<DataBaseContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Broker de mensagens (RabbitMQ), partilhado por todos os pedidos
builder.Services.AddSingleton<RabbitMQService>();

builder.Services.AddGrpc();

var app = builder.Build();

// Cria/atualiza o esquema da base de dados e, em desenvolvimento, cria utilizadores de demonstração
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DataBaseContext>();
    db.Database.Migrate();

    if (app.Environment.IsDevelopment() && !db.CLients.Any())
    {
        db.CLients.AddRange(
            new CLient { Client_ID = "admin", password = "admin", Admin = true },
            new CLient { Client_ID = "alice", password = "alice" },
            new CLient { Client_ID = "bob", password = "bob" });
        db.SaveChanges();
        app.Logger.LogInformation("Utilizadores de demonstração criados: admin, alice, bob");
    }
}

app.MapGrpcService<GreeterService>();
app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client.");

app.Run();
