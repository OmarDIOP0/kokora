using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kokora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OfficialManOfTheMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "man_of_the_match_player_id",
                table: "matches",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_matches_man_of_the_match_player_id",
                table: "matches",
                column: "man_of_the_match_player_id");

            migrationBuilder.AddForeignKey(
                name: "fk_matches_players_man_of_the_match_player_id",
                table: "matches",
                column: "man_of_the_match_player_id",
                principalTable: "players",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_matches_players_man_of_the_match_player_id",
                table: "matches");

            migrationBuilder.DropIndex(
                name: "ix_matches_man_of_the_match_player_id",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "man_of_the_match_player_id",
                table: "matches");
        }
    }
}
