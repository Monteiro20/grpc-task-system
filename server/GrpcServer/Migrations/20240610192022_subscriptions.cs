using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace gRPC_server.Migrations
{
    /// <inheritdoc />
    public partial class subscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "subscriptions",
                table: "CLients",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "subscriptions",
                table: "CLients");
        }
    }
}
