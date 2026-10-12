using System.Collections.Concurrent;
using System.Text;
using Grpc.Net.Client;
using gRPC_server;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

/// <summary>
/// Cliente RabbitMQ no padrão RPC: publica um pedido na fila "na" com uma fila de resposta
/// exclusiva e um correlation ID, e devolve uma Task que termina quando chega a resposta.
/// O servidor usa este mecanismo para notificar clientes subscritos de novas tarefas.
/// </summary>
public class RpcClient : IDisposable
{
    private const string RequestQueue = "na";

    private readonly IConnection connection;
    private readonly IModel channel;
    private readonly string replyQueueName;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> callbackMapper = new();

    public RpcClient(string hostName)
    {
        var factory = new ConnectionFactory { HostName = hostName };
        connection = factory.CreateConnection();
        channel = connection.CreateModel();

        // Fila de resposta temporária e exclusiva deste cliente
        replyQueueName = channel.QueueDeclare().QueueName;
        var consumer = new EventingBasicConsumer(channel);
        consumer.Received += (model, ea) =>
        {
            if (!callbackMapper.TryRemove(ea.BasicProperties.CorrelationId, out var tcs))
                return;
            tcs.TrySetResult(Encoding.UTF8.GetString(ea.Body.ToArray()));
        };

        channel.BasicConsume(consumer: consumer, queue: replyQueueName, autoAck: true);
    }

    public Task<string> CallAsync(CancellationToken cancellationToken = default)
    {
        IBasicProperties props = channel.CreateBasicProperties();
        var correlationId = Guid.NewGuid().ToString();
        props.CorrelationId = correlationId;
        props.ReplyTo = replyQueueName;

        var tcs = new TaskCompletionSource<string>();
        callbackMapper.TryAdd(correlationId, tcs);

        channel.BasicPublish(exchange: string.Empty,
            routingKey: RequestQueue,
            basicProperties: props,
            body: Encoding.UTF8.GetBytes("1"));

        cancellationToken.Register(() => callbackMapper.TryRemove(correlationId, out _));
        return tcs.Task;
    }

    public void Dispose()
    {
        channel?.Dispose();
        connection?.Dispose();
    }
}

public class Client
{
    // Configuráveis por variáveis de ambiente
    private static readonly string ServerUrl = Environment.GetEnvironmentVariable("GRPC_SERVER_URL") ?? "https://localhost:7140";
    private static readonly string RabbitHost = Environment.GetEnvironmentVariable("RABBITMQ_HOST") ?? "localhost";

    public static async Task Main(string[] args)
    {
        using var grpcChannel = GrpcChannel.ForAddress(ServerUrl);
        var client = new Greeter.GreeterClient(grpcChannel);

        bool login = false;
        string? user = null;
        string? quitMessage = null;
        string? subscriptions = null;
        Thread? notificationThread = null;

        while (quitMessage != "quit")
        {
            while (!login)
            {
                Console.WriteLine("Escreva o ID do cliente:");
                user = Console.ReadLine();
                Console.WriteLine("Insira a password:");
                string? password = Console.ReadLine();

                var reply = await client.SayLoginAsync(new loginRequest { Client = user ?? "", PasswordLogin = password ?? "" });
                login = reply.Login;
                subscriptions = reply.Subscriptins;
                Console.WriteLine(login ? reply.Message : "Credenciais erradas");
            }

            if (user == "admin")
            {
                while (login)
                {
                    Console.WriteLine("Escreva a mensagem:");
                    string? message = Console.ReadLine();
                    if (message == "1")
                    {
                        Console.WriteLine("Escreva a descrição da tarefa que quer adicionar:");
                        var description = Console.ReadLine();
                        var reply = await client.addTaskAsync(new TaskRequest { Client = user, Message = description ?? "" });
                        Console.WriteLine(reply.Message);
                    }
                    else if (message == "3")
                    {
                        login = false;
                    }
                    else
                    {
                        var reply = await client.SayHelloAsync(new HelloRequest { Message = message ?? "", Client = user });
                        quitMessage = reply.Message;
                        Console.WriteLine(quitMessage == "quit" ? "Adeus" : reply.Message);
                        if (reply.Client == "null")
                        {
                            login = false;
                        }
                    }
                }
            }
            else
            {
                // Clientes subscritos dos Serviços A ficam à escuta de novas tarefas numa thread
                // em segundo plano, enquanto a thread principal continua a tratar o menu.
                if (subscriptions == "1" && notificationThread == null)
                {
                    notificationThread = new Thread(ListenForNewTasks) { IsBackground = true };
                    notificationThread.Start();
                }

                while (login)
                {
                    Console.WriteLine("Escreva a mensagem:");
                    string? message = Console.ReadLine();
                    if (message == "7")
                    {
                        var available = await client.choseTaskAsync(new ChoseTaskRequest { Message = message, Client = user ?? "" });
                        Console.WriteLine(available.Message);
                        Console.WriteLine("Escolha a tarefa (ID):");
                        var choice = Console.ReadLine();

                        var result = await client.choseTaskAsync(new ChoseTaskRequest { Client = user ?? "", Message = choice ?? "" });
                        Console.WriteLine(result.Message);
                    }
                    else
                    {
                        var reply = await client.SayHelloAsync(new HelloRequest { Message = message ?? "", Client = user ?? "" });
                        quitMessage = reply.Message;
                        Console.WriteLine(quitMessage == "quit" ? "Adeus" : reply.Message);
                        if (reply.Client == "null")
                        {
                            login = false;
                        }
                        if (quitMessage == "quit")
                        {
                            break;
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Corre em segundo plano: pede ao RabbitMQ uma notificação e mostra-a quando o admin
    /// cria uma nova tarefa. Repete enquanto a aplicação estiver aberta.
    /// </summary>
    private static void ListenForNewTasks()
    {
        try
        {
            using var rpcClient = new RpcClient(RabbitHost);
            while (true)
            {
                var task = rpcClient.CallAsync().GetAwaiter().GetResult();
                Console.WriteLine("----------------\n" +
                                  "NOTIFICAÇÃO: Nova tarefa adicionada à base de dados\n" +
                                  "Tarefa: " + task + "\n" +
                                  "----------------");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[notificações indisponíveis: {ex.Message}]");
        }
    }
}
