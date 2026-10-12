using gRPC_server.Models;
using Microsoft.EntityFrameworkCore;

namespace gRPC_server.Data
{
    public class DataBaseContext : DbContext
    {
        public DataBaseContext(DbContextOptions<DataBaseContext> options) : base(options)
        {

        }
        public DbSet<CLient> CLients { get; set; }
        public DbSet<Models.TasksForClients> Tasks { get; set; }
        public DbSet<Tasks_Client> tasks_Clients { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

        }
    }

}
