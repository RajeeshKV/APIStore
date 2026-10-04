using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KromicCommerce.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupportDesk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reference-number allocator shared by tickets and invoices.
            //
            // Created here rather than lazily on first use because
            // AppDbContext.NextTicketReferenceSequenceAsync issues a bare nextval() with no
            // existence check, so a missing sequence would surface as a raw SQL error on the
            // customer's first ticket rather than as a diagnosable startup failure. A
            // count-based "SELECT MAX(reference) + 1" would be wrong under concurrency: two
            // simultaneous creates both observe N and both try to store N+1, and one of them
            // fails the unique index with a 500.
            migrationBuilder.Sql(
                """
                CREATE SEQUENCE IF NOT EXISTS ticket_reference_seq
                    AS bigint
                    START WITH 1
                    INCREMENT BY 1
                    MINVALUE 1
                    NO MAXVALUE
                    CACHE 50;
                """);

            migrationBuilder.CreateTable(
                name: "invoice_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IssuerNameOverride = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    TaxId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Terms = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    FooterNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    AccentColor = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    ShowIssuerIdentity = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    ShowLineItemTable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    ShowTerms = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    ShowNotes = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "support_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AutoCloseIdleHours = table.Column<int>(type: "integer", nullable: false, defaultValue: 72),
                    AutomatedInvoiceMailingEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    InvoiceMailSubjectOverride = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    AutoGenerateInvoiceOnResolve = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    NotifyAdminOnTicketCreated = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    NotifyAdminOnTicketReopened = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    NotifyCustomerOnTicketResolved = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    MaxAttachmentsPerComment = table.Column<int>(type: "integer", nullable: false, defaultValue: 6),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_support_settings", x => x.Id);
                    table.CheckConstraint("ck_support_settings_singleton", "\"Id\" = '00000000-0000-0000-0000-0000000000f1'");
                });

            migrationBuilder.CreateTable(
                name: "tickets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Priority = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RelatedOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    AssignedAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    FirstResponseAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastReopenedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReopenCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LastActivityAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUserActivityAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AutoCloseAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AutoCloseIdleHours = table.Column<int>(type: "integer", nullable: false, defaultValue: 72),
                    LatestInvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tickets", x => x.Id);
                    table.CheckConstraint("ck_tickets_idle_hours", "\"AutoCloseIdleHours\" >= 1");
                    table.CheckConstraint("ck_tickets_reopen_count", "\"ReopenCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_tickets_orders_RelatedOrderId",
                        column: x => x.RelatedOrderId,
                        principalTable: "orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tickets_users_AssignedAdminId",
                        column: x => x.AssignedAdminId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_tickets_users_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentCommentId = table.Column<Guid>(type: "uuid", nullable: true),
                    AuthorId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsAdminAuthor = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                    Depth = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ThreadPath = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    InternalNote = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_comments", x => x.Id);
                    table.CheckConstraint("ck_ticket_comments_depth", "\"Depth\" >= 0 AND \"Depth\" <= 6");
                    table.CheckConstraint("ck_ticket_comments_parent_link", "\"Depth\" = 0 AND \"ParentCommentId\" IS NULL OR \"Depth\" > 0 AND \"ParentCommentId\" IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_ticket_comments_ticket_comments_ParentCommentId",
                        column: x => x.ParentCommentId,
                        principalTable: "ticket_comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ticket_comments_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ticket_comments_users_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ticket_invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    TemplateId = table.Column<Guid>(type: "uuid", nullable: true),
                    issuer_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    issuer_address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    issuer_email = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    issuer_tax_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    bill_to_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    bill_to_email = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    bill_to_address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    invoice_date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    due_date_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    currency_code = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric", nullable: false),
                    discount_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    tax_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    shipping_amount = table.Column<decimal>(type: "numeric", nullable: false),
                    cod_fee = table.Column<decimal>(type: "numeric", nullable: false),
                    grand_total = table.Column<decimal>(type: "numeric", nullable: false),
                    line_items_json = table.Column<string>(type: "text", nullable: false),
                    ticket_number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ticket_subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    order_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    terms = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    footer_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    accent_color = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    QueuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GeneratedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GenerationAttempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    PdfContent = table.Column<byte[]>(type: "bytea", nullable: true),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    Checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsContentOverridden = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedByAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    OverriddenByAdminId = table.Column<Guid>(type: "uuid", nullable: true),
                    OverriddenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EmailedToCustomer = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    EmailedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_invoices", x => x.Id);
                    table.CheckConstraint("ck_ticket_invoices_attempts", "\"GenerationAttempts\" >= 0 AND \"GenerationAttempts\" <= 3");
                    table.CheckConstraint("ck_ticket_invoices_revision", "\"Revision\" >= 1");
                    table.ForeignKey(
                        name: "FK_ticket_invoices_invoice_templates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "invoice_templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ticket_invoices_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ticket_invoices_users_CreatedByAdminId",
                        column: x => x.CreatedByAdminId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ticket_invoices_users_OverriddenByAdminId",
                        column: x => x.OverriddenByAdminId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ticket_status_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ToStatus = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Actor = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_status_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ticket_status_history_tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ticket_status_history_users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ticket_attachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketCommentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    PublicId = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    SecureUrl = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Format = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ContentType = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Width = table.Column<int>(type: "integer", nullable: true),
                    Height = table.Column<int>(type: "integer", nullable: true),
                    DurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    AltText = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ticket_attachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ticket_attachments_ticket_comments_TicketCommentId",
                        column: x => x.TicketCommentId,
                        principalTable: "ticket_comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_invoice_templates_name",
                table: "invoice_templates",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_templates_single_default",
                table: "invoice_templates",
                column: "IsDefault",
                unique: true,
                filter: "\"IsDefault\" = true");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_attachments_comment_sort",
                table: "ticket_attachments",
                columns: new[] { "TicketCommentId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_comments_AuthorId",
                table: "ticket_comments",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_comments_parent",
                table: "ticket_comments",
                column: "ParentCommentId");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_comments_ticket_path",
                table: "ticket_comments",
                columns: new[] { "TicketId", "ThreadPath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ticket_invoices_CreatedByAdminId",
                table: "ticket_invoices",
                column: "CreatedByAdminId");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_invoices_invoice_number",
                table: "ticket_invoices",
                column: "InvoiceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ticket_invoices_OverriddenByAdminId",
                table: "ticket_invoices",
                column: "OverriddenByAdminId");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_invoices_status_queued",
                table: "ticket_invoices",
                columns: new[] { "Status", "QueuedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ticket_invoices_TemplateId",
                table: "ticket_invoices",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_invoices_ticket_revision",
                table: "ticket_invoices",
                columns: new[] { "TicketId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ticket_status_history_ActorId",
                table: "ticket_status_history",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "ix_ticket_status_history_ticket_occurred",
                table: "ticket_status_history",
                columns: new[] { "TicketId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_tickets_AssignedAdminId",
                table: "tickets",
                column: "AssignedAdminId");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_autoclose_deadline",
                table: "tickets",
                column: "AutoCloseAtUtc",
                filter: "\"AutoCloseAtUtc\" IS NOT NULL AND \"Status\" = 'Resolved'");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_customer_activity",
                table: "tickets",
                columns: new[] { "CustomerId", "LastActivityAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_related_order",
                table: "tickets",
                column: "RelatedOrderId");

            migrationBuilder.CreateIndex(
                name: "ix_tickets_status_activity",
                table: "tickets",
                columns: new[] { "Status", "LastActivityAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_status_created",
                table: "tickets",
                columns: new[] { "Status", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "ix_tickets_ticket_number",
                table: "tickets",
                column: "TicketNumber",
                unique: true);

            // -----------------------------------------------------------------------
            // Seed rows
            //
            // Both are configuration defaults that the application expects to already exist.
            // SupportSettingsProvider.GetOrCreateAsync would create the settings row on first
            // read, but that is a read path racing a write path: two concurrent first requests
            // both observe "missing" and both insert, and the check constraint then rejects the
            // second with an error surfaced to a customer. Seeding removes the race entirely.
            // -----------------------------------------------------------------------

            migrationBuilder.InsertData(
                table: "support_settings",
                columns: new[]
                {
                    "Id", "AutoCloseIdleHours", "AutomatedInvoiceMailingEnabled",
                    "InvoiceMailSubjectOverride", "AutoGenerateInvoiceOnResolve",
                    "NotifyAdminOnTicketCreated", "NotifyAdminOnTicketReopened",
                    "NotifyCustomerOnTicketResolved", "MaxAttachmentsPerComment",
                    "CreatedAtUtc", "UpdatedAtUtc", "CreatedBy", "UpdatedBy"
                },
                values: new object[]
                {
                    new Guid("00000000-0000-0000-0000-0000000000f1"), // SupportSettings.SingletonId
                    72,
                    true,
                    null,
                    true,
                    true,
                    true,
                    true,
                    6,
                    SeedTimestamp,
                    SeedTimestamp,
                    "SupportDesk",
                    "SupportDesk"
                });

            // The default invoice template. ResolveTicketHandler and TicketInvoiceWorker both
            // fall back to "the row with IsDefault = true" when no template is named
            // explicitly, so without this seed an ordinary resolution would render with an
            // empty layout. The fixed id lets a deployment recognise the built-in row.
            migrationBuilder.InsertData(
                table: "invoice_templates",
                columns: new[]
                {
                    "Id", "Name", "Description", "IsDefault", "IssuerNameOverride", "TaxId",
                    "Notes", "Terms", "FooterNote", "AccentColor",
                    "ShowIssuerIdentity", "ShowLineItemTable", "ShowTerms", "ShowNotes",
                    "CreatedAtUtc", "UpdatedAtUtc", "CreatedBy", "UpdatedBy"
                },
                values: new object[]
                {
                    DefaultInvoiceTemplateId,
                    "Standard Invoice",
                    "Standard store invoice. Applied automatically to every generated invoice.",
                    true,
                    null,
                    null,
                    "Thank you for your business.",
                    "Payment is due as per the agreed terms. Please quote the invoice number on any remittance.",
                    "This is a computer-generated invoice and does not require a signature.",
                    "1A1A1A",
                    true,
                    true,
                    true,
                    true,
                    SeedTimestamp,
                    SeedTimestamp,
                    "SupportDesk",
                    "SupportDesk"
                });
        }

        /// <summary>
        /// Fixed id of the seeded default template. Stable so a deployment can detect that the
        /// built-in layout is still present (and unedited) across upgrades.
        /// </summary>
        private static readonly Guid DefaultInvoiceTemplateId =
            new("00000000-0000-0000-0000-0000000000f2");

        /// <summary>
        /// Fixed audit timestamp for seeded rows. Deliberately not DateTime.UtcNow: a
        /// timestamp baked into a migration file must never change, or the migration stops
        /// being idempotent and the same id carries a different value on each re-apply.
        /// </summary>
        private static readonly DateTime SeedTimestamp =
            new(2026, 10, 4, 0, 0, 0, DateTimeKind.Utc);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "support_settings");

            migrationBuilder.DropTable(
                name: "ticket_attachments");

            migrationBuilder.DropTable(
                name: "ticket_invoices");

            migrationBuilder.DropTable(
                name: "ticket_status_history");

            migrationBuilder.DropTable(
                name: "ticket_comments");

            migrationBuilder.DropTable(
                name: "invoice_templates");

            migrationBuilder.DropTable(
                name: "tickets");

            // Tables first, then the sequence: the ticket tables do not depend on the sequence,
            // but dropping it last keeps the rollback readable from the outside in.
            migrationBuilder.Sql("DROP SEQUENCE IF EXISTS ticket_reference_seq;");
        }
    }
}
