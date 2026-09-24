using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kokora.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountsAndPush : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<int>>(
                name: "club_ids",
                table: "push_subscriptions",
                type: "integer[]",
                nullable: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "notified_at",
                table: "articles",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "club_ids",
                table: "push_subscriptions");

            migrationBuilder.DropColumn(
                name: "notified_at",
                table: "articles");
        }
    }
}
