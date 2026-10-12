using System.ComponentModel.DataAnnotations;

namespace gRPC_server.Models
{
    public class CLient
    {
        [Key]
        public string Client_ID { get; set; }
        public string password { get; set; }
        public int mota {  get; set; }
        public bool Admin { get; set; }
        public int subscriptions { get; set; }
    }
}
