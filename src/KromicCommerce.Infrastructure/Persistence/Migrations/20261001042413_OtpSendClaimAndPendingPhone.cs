using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KromicCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OtpSendClaimAndPendingPhone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PendingPhoneNumber",
                table: "users",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "otp_send_claims",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PhoneNumber = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_otp_send_claims", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_otp_send_claims_phone_purpose",
                table: "otp_send_claims",
                columns: new[] { "PhoneNumber", "Purpose" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "otp_send_claims");

            migrationBuilder.DropColumn(
                name: "PendingPhoneNumber",
                table: "users");
        }
    }
}
