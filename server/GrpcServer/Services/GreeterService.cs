using System.Text;
using Grpc.Core;
using gRPC_server.Broker;
using gRPC_server.Data;
using gRPC_server.Models;

namespace gRPC_server.Services
{
    /// <summary>
    /// Serviço gRPC: autenticação, gestão de tarefas e subscrições.
    ///
    /// Estados de uma tarefa (TasksForClients.state):
    ///   2 = disponível, 1 = atribuída a um cliente, 0 = concluída.
    ///
    /// Subscrições (CLient.subscriptions): 0 = nenhuma, 1 = Serviços A,
    ///   2 = outro serviço, 3 = Serviços A + outro serviço.
    /// </summary>
    public class GreeterService : Greeter.GreeterBase
    {
        private const int TaskCompleted = 0;
        private const int TaskAssigned = 1;
        private const int TaskAvailable = 2;

        private const string ClientMenu =
            "\nPressione 1 para ver a tarefa que tem alocada" +
            "\nPressione 2 para concluir a tarefa que tem alocada" +
            "\nPressione 4 para ver as tarefas que já concluiu" +
            "\nPressione 5 para deslogar" +
            "\nPressione 6 para subscrever os Serviços A (notificações de novas tarefas)" +
            "\nPressione 7 para escolher uma tarefa disponível" +
            "\nEscreva \"quit\" para desconectar do servidor";

        private const string AdminMenu =
            "\nPressione 1 para criar uma nova tarefa" +
            "\nPressione 2 para adicionar uma nova mota" +
            "\nPressione 3 para deslogar";

        private readonly DataBaseContext _contextDb;
        private readonly ILogger<GreeterService> _logger;
        private readonly RabbitMQService _rabbitMQService;

        public GreeterService(ILogger<GreeterService> logger, DataBaseContext context, RabbitMQService rabbitMQService)
        {
            _logger = logger;
            _contextDb = context;
            _rabbitMQService = rabbitMQService;
        }

        public override Task<loginReply> SayLogin(loginRequest request, ServerCallContext context)
        {
            var user = _contextDb.CLients.FirstOrDefault(x => x.Client_ID == request.Client && x.password == request.PasswordLogin);
            if (user == null)
            {
                return Task.FromResult(new loginReply { Login = false, Admin = false, Message = "null" });
            }

            _logger.LogInformation("Login: {Client} (admin: {Admin})", user.Client_ID, user.Admin);

            if (user.Admin)
            {
                return Task.FromResult(new loginReply
                {
                    Login = true,
                    Admin = true,
                    Message = "HELLO ADMIN" + AdminMenu,
                    Subscriptins = "null"
                });
            }

            return Task.FromResult(new loginReply
            {
                Login = true,
                Admin = false,
                Message = ClientMenu,
                Subscriptins = user.subscriptions.ToString()
            });
        }

        public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
        {
            return Task.FromResult(request.Client == "admin"
                ? HandleAdminCommand(request)
                : HandleClientCommand(request));
        }

        private HelloReply HandleAdminCommand(HelloRequest request)
        {
            // A opção 1 (criar tarefa) usa o RPC addTask e a 3 (logout) é tratada no cliente
            if (request.Message == "2")
            {
                try
                {
                    // Atribui o próximo número de mota ao primeiro cliente que ainda não tem nenhuma
                    var user = _contextDb.CLients.FirstOrDefault(x => x.mota == 0 && x.Client_ID != "admin");
                    if (user == null)
                    {
                        return Reply("Todos os clientes já têm uma mota atribuída." + AdminMenu, "admin");
                    }

                    user.mota = _contextDb.CLients.Count(x => x.mota != 0) + 1;
                    _contextDb.SaveChanges();
                    return Reply($"Mota {user.mota} atribuída ao cliente {user.Client_ID}." + AdminMenu, "admin");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro ao atribuir mota");
                    return Reply(ex.Message, "admin");
                }
            }

            return Reply("Opção inválida." + AdminMenu, request.Client);
        }

        private HelloReply HandleClientCommand(HelloRequest request)
        {
            switch (request.Message)
            {
                case "1": // ver a tarefa alocada
                    return Reply(ListClientTasks(request.Client, TaskAssigned, "O cliente não tem nenhuma tarefa alocada.") + ClientMenu, request.Client);

                case "2": // concluir a tarefa alocada
                    return Reply(CompleteAssignedTask(request.Client) + ClientMenu, request.Client);

                case "4": // ver tarefas concluídas
                    return Reply(ListClientTasks(request.Client, TaskCompleted, "O cliente ainda não concluiu nenhuma tarefa.") + ClientMenu, request.Client);

                case "5": // deslogar
                    return Reply("Deslogado", "null");

                case "6": // subscrever os Serviços A
                    return Reply(SubscribeServicesA(request.Client) + ClientMenu, request.Client);

                case "quit":
                    return Reply("quit", request.Client);

                default:
                    return Reply("Mensagem errada." + ClientMenu, request.Client);
            }
        }

        private string ListClientTasks(string clientId, int state, string emptyMessage)
        {
            var taskIds = _contextDb.tasks_Clients
                .Where(x => x.Client_ID == clientId)
                .Select(x => x.Task_ID)
                .Distinct()
                .ToList();

            var descriptions = _contextDb.Tasks
                .Where(t => taskIds.Contains(t.taskId) && t.state == state)
                .Select(t => t.description)
                .ToList();

            return descriptions.Count == 0 ? emptyMessage : string.Join("\n", descriptions);
        }

        private string CompleteAssignedTask(string clientId)
        {
            var taskIds = _contextDb.tasks_Clients
                .Where(x => x.Client_ID == clientId)
                .Select(x => x.Task_ID)
                .ToList();

            var task = _contextDb.Tasks.FirstOrDefault(t => taskIds.Contains(t.taskId) && t.state == TaskAssigned);
            if (task == null)
            {
                return "O cliente não tem nenhuma tarefa alocada.";
            }

            task.state = TaskCompleted;
            _contextDb.SaveChanges();
            return $"Tarefa **{task.description}** concluída.";
        }

        private string SubscribeServicesA(string clientId)
        {
            var user = _contextDb.CLients.Find(clientId);
            if (user == null)
            {
                return "Cliente não encontrado.";
            }

            switch (user.subscriptions)
            {
                case 1:
                case 3:
                    return "O cliente já está subscrito nesse serviço.";
                case 0:
                    user.subscriptions = 1;
                    break;
                case 2:
                    user.subscriptions = 3;
                    break;
                default:
                    return "Estado de subscrição inválido.";
            }

            _contextDb.SaveChanges();
            return "Subscrição no serviço efetuada.";
        }

        public override Task<TaskReply> addTask(TaskRequest request, ServerCallContext context)
        {
            try
            {
                // Gera o próximo ID livre: SA_T1, SA_T2, ...
                int id = 1;
                var taskId = "SA_T" + id;
                while (_contextDb.Tasks.Find(taskId) != null)
                {
                    id++;
                    taskId = "SA_T" + id;
                }

                _contextDb.Add(new TasksForClients
                {
                    taskId = taskId,
                    description = request.Message,
                    state = TaskAvailable
                });
                _contextDb.SaveChanges();

                // A notificação corre numa thread separada: fica à espera de um cliente subscrito
                // no RabbitMQ sem bloquear a resposta gRPC ao admin.
                var description = request.Message;
                var notifier = new Thread(() => NotifySubscriber(description)) { IsBackground = true };
                notifier.Start();

                return Task.FromResult(new TaskReply { Message = "Tarefa adicionada à base de dados", Client = "admin" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao adicionar tarefa");
                return Task.FromResult(new TaskReply { Message = ex.Message, Client = "admin" });
            }
        }

        public override Task<choseTaskReply> choseTask(ChoseTaskRequest request, ServerCallContext context)
        {
            // "7": lista as tarefas disponíveis
            if (request.Message == "7")
            {
                var available = _contextDb.Tasks.Where(x => x.state == TaskAvailable).ToList();
                var sb = new StringBuilder();
                foreach (var task in available)
                {
                    sb.AppendLine($"{task.description}; id: {task.taskId}");
                }

                var list = sb.Length == 0 ? "Não há tarefas disponíveis." : sb.ToString();
                return Task.FromResult(new choseTaskReply { Message = list, Client = request.Client });
            }

            // Caso contrário, a mensagem é o ID da tarefa escolhida
            try
            {
                var task = _contextDb.Tasks.Find(request.Message);
                if (task == null)
                {
                    return ChoseReply("Tarefa não encontrada." + ClientMenu, request.Client);
                }

                if (task.state != TaskAvailable)
                {
                    return ChoseReply("Essa tarefa já não está disponível." + ClientMenu, request.Client);
                }

                // Um cliente só pode ter uma tarefa atribuída de cada vez
                var userTaskIds = _contextDb.tasks_Clients
                    .Where(x => x.Client_ID == request.Client)
                    .Select(x => x.Task_ID)
                    .ToList();
                bool hasActiveTask = _contextDb.Tasks.Any(t => userTaskIds.Contains(t.taskId) && t.state == TaskAssigned);
                if (hasActiveTask)
                {
                    return ChoseReply("Já possui uma tarefa atribuída." + ClientMenu, request.Client);
                }

                _contextDb.Add(new Tasks_Client { Task_ID = task.taskId, Client_ID = request.Client });
                task.state = TaskAssigned;
                _contextDb.SaveChanges();

                return ChoseReply("Tarefa atribuída com sucesso." + ClientMenu, request.Client);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao atribuir tarefa");
                return ChoseReply(ex.Message, request.Client);
            }
        }

        private void NotifySubscriber(string description)
        {
            try
            {
                _rabbitMQService.PublishMessageQueue(description).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro ao enviar notificação via RabbitMQ");
            }
        }

        private static HelloReply Reply(string message, string client) =>
            new HelloReply { Message = message, Client = client };

        private static Task<choseTaskReply> ChoseReply(string message, string client) =>
            Task.FromResult(new choseTaskReply { Message = message, Client = client });
    }
}
