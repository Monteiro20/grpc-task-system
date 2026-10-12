using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace gRPC_server.Models
{
    public class Tasks_Client
    {
        [Key]
        public long? ID { get; set; }

        public string Task_ID { get; set; }
        [ForeignKey("Client")]
        public string Client_ID { get; set; }
    }
}
