using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kokora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LiveClientKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "client_key",
                table: "match_events",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_match_events_match_id_client_key",
                table: "match_events",
                columns: new[] { "match_id", "client_key" },
                unique: true,
                filter: "client_key IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_match_events_match_id_client_key",
                table: "match_events");

            migrationBuilder.DropColumn(
                name: "client_key",
                table: "match_events");
        }
    }
}
