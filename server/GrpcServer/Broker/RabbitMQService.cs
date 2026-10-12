using System.Text;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace gRPC_server.Broker
{
    /// <summary>
    /// Notificação de novas tarefas através do RabbitMQ (padrão RPC com reply-to e correlation ID).
    ///
    /// Os clientes subscritos publicam um pedido na fila "na" com uma fila de resposta própria
    /// e ficam à espera. Quando o admin cria uma tarefa, o servidor consome UM desses pedidos
    /// e responde com a descrição da tarefa, notificando um cliente de cada vez.
    /// </summary>
    public class RabbitMQService
    {
        private const string RequestQueue = "na";
        private readonly string _hostName;

        public RabbitMQService(IConfiguration configuration)
        {
            _hostName = configuration["RabbitMQ:HostName"] ?? "localhost";
        }

        public async Task PublishMessageQueue(string task)
        {
            var factory = new ConnectionFactory { HostName = _hostName };
            using var connection = factory.CreateConnection();
            using var channel = connection.CreateModel();

            channel.QueueDeclare(queue: RequestQueue,
                durable: false,
                exclusive: false,
                autoDelete: false,
                arguments: null);

            // Entrega no máximo uma mensagem de cada vez a este consumidor
            channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

            var consumer = new EventingBasicConsumer(channel);
            var processed = new TaskCompletionSource<bool>();

            consumer.Received += async (model, ea) =>
            {
                await ProcessMessageAsync(channel, ea, task);
                processed.TrySetResult(true);
            };

            channel.BasicConsume(queue: RequestQueue, autoAck: false, consumer: consumer);
            Console.WriteLine(" [x] À espera de um cliente subscrito...");

            // Espera até um pedido ser processado antes de fechar a ligação
            await processed.Task;
            Console.WriteLine(" [x] Notificação enviada.");
        }

        private static async Task ProcessMessageAsync(IModel channel, BasicDeliverEventArgs ea, string task)
        {
            var response = string.Empty;
            var props = ea.BasicProperties;
            var replyProps = channel.CreateBasicProperties();
            replyProps.CorrelationId = props.CorrelationId;

            try
            {
                var message = Encoding.UTF8.GetString(ea.Body.ToArray());
                Console.WriteLine($" [.] Pedido recebido: {message}");
                response = task;
            }
            catch (Exception ex)
            {
                Console.WriteLine($" [.] {ex.Message}");
                response = ex.Message;
            }
            finally
            {
                // Responde para a fila indicada pelo cliente e confirma a mensagem
                channel.BasicPublish(exchange: string.Empty,
                    routingKey: props.ReplyTo,
                    basicProperties: replyProps,
                    body: Encoding.UTF8.GetBytes(response));
                channel.BasicAck(deliveryTag: ea.DeliveryTag, multiple: false);
            }

            await Task.Delay(100);
        }
    }
}
