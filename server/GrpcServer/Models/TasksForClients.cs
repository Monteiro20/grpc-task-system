using System.ComponentModel.DataAnnotations;

namespace gRPC_server.Models
{
    public class TasksForClients
    {
        [Key]
        public string taskId {  get; set; }
        public string description { get; set; }
        public int state { get; set; }
    }
}
