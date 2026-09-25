using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KromicCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase8_UsernamePasswordReset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedUsername",
                table: "users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordResetTokenExpiresAt",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordResetTokenHash",
                table: "users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "users",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "promotions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_username",
                table: "users",
                column: "NormalizedUsername",
                unique: true,
                filter: "\"NormalizedUsername\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_normalized_username",
                table: "users");

            migrationBuilder.DropColumn(
                name: "NormalizedUsername",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PasswordResetTokenExpiresAt",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PasswordResetTokenHash",
                table: "users");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "users");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "promotions");
        }
    }
}
