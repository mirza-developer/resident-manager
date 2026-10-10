using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ResidentialComplex.Persistence.Migrations;

/// <summary>
/// Online payment support (Zibal): PaymentAttempts (one row per gateway session / try) and
/// PaymentAttemptEvents (append-only trace of every gateway interaction).
/// TrackId is unique only among non-null values (filtered index on SQL Server; SQLite already
/// treats NULLs as distinct).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20250101000006_AddPaymentAttempts")]
public class AddPaymentAttempts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            migrationBuilder.Sql(@"
                CREATE TABLE [PaymentAttempts] (
                    [Id] INT NOT NULL IDENTITY(1, 1) CONSTRAINT [PK_PaymentAttempts] PRIMARY KEY,
                    [PublicId] UNIQUEIDENTIFIER NOT NULL,
                    [BillId] INT NOT NULL,
                    [Amount] DECIMAL(18,2) NOT NULL,
                    [Gateway] NVARCHAR(50) NOT NULL,
                    [OrderId] NVARCHAR(100) NOT NULL,
                    [TrackId] BIGINT NULL,
                    [Status] INT NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL,
                    [UpdatedAt] DATETIME2 NOT NULL,
                    [RequestedAt] DATETIME2 NULL,
                    [CallbackReceivedAt] DATETIME2 NULL,
                    [VerifiedAt] DATETIME2 NULL,
                    [PaidAt] DATETIME2 NULL,
                    [RequestResultCode] INT NULL,
                    [CallbackSuccess] BIT NULL,
                    [CallbackStatus] INT NULL,
                    [ZibalStatus] INT NULL,
                    [RefNumber] BIGINT NULL,
                    [CardNumber] NVARCHAR(30) NULL,
                    [PaidAmount] DECIMAL(18,2) NULL,
                    [Note] NVARCHAR(1000) NULL,
                    [InitiatedByUserId] NVARCHAR(450) NULL,
                    [PaymentId] INT NULL,
                    CONSTRAINT [FK_PaymentAttempts_Bills_BillId] FOREIGN KEY ([BillId]) REFERENCES [Bills] ([Id]),
                    CONSTRAINT [FK_PaymentAttempts_Payments_PaymentId] FOREIGN KEY ([PaymentId]) REFERENCES [Payments] ([Id])
                );
                CREATE UNIQUE INDEX [IX_PaymentAttempts_PublicId] ON [PaymentAttempts] ([PublicId]);
                CREATE UNIQUE INDEX [IX_PaymentAttempts_OrderId] ON [PaymentAttempts] ([OrderId]);
                CREATE UNIQUE INDEX [IX_PaymentAttempts_TrackId] ON [PaymentAttempts] ([TrackId]) WHERE [TrackId] IS NOT NULL;
                CREATE INDEX [IX_PaymentAttempts_BillId] ON [PaymentAttempts] ([BillId]);
                CREATE INDEX [IX_PaymentAttempts_Status] ON [PaymentAttempts] ([Status]);
                CREATE INDEX [IX_PaymentAttempts_PaymentId] ON [PaymentAttempts] ([PaymentId]);
            ");

            migrationBuilder.Sql(@"
                CREATE TABLE [PaymentAttemptEvents] (
                    [Id] INT NOT NULL IDENTITY(1, 1) CONSTRAINT [PK_PaymentAttemptEvents] PRIMARY KEY,
                    [PaymentAttemptId] INT NOT NULL,
                    [CreatedAt] DATETIME2 NOT NULL,
                    [EventType] INT NOT NULL,
                    [ResultCode] INT NULL,
                    [ZibalStatus] INT NULL,
                    [Message] NVARCHAR(1000) NULL,
                    [Data] NVARCHAR(MAX) NULL,
                    CONSTRAINT [FK_PaymentAttemptEvents_PaymentAttempts_PaymentAttemptId] FOREIGN KEY ([PaymentAttemptId]) REFERENCES [PaymentAttempts] ([Id]) ON DELETE CASCADE
                );
                CREATE INDEX [IX_PaymentAttemptEvents_PaymentAttemptId] ON [PaymentAttemptEvents] ([PaymentAttemptId]);
            ");
        }
        else
        {
            // SQLite (used in tests / development)
            migrationBuilder.Sql(@"
                CREATE TABLE ""PaymentAttempts"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_PaymentAttempts"" PRIMARY KEY AUTOINCREMENT,
                    ""PublicId"" TEXT NOT NULL,
                    ""BillId"" INTEGER NOT NULL,
                    ""Amount"" TEXT NOT NULL,
                    ""Gateway"" TEXT NOT NULL,
                    ""OrderId"" TEXT NOT NULL,
                    ""TrackId"" INTEGER NULL,
                    ""Status"" INTEGER NOT NULL,
                    ""CreatedAt"" TEXT NOT NULL,
                    ""UpdatedAt"" TEXT NOT NULL,
                    ""RequestedAt"" TEXT NULL,
                    ""CallbackReceivedAt"" TEXT NULL,
                    ""VerifiedAt"" TEXT NULL,
                    ""PaidAt"" TEXT NULL,
                    ""RequestResultCode"" INTEGER NULL,
                    ""CallbackSuccess"" INTEGER NULL,
                    ""CallbackStatus"" INTEGER NULL,
                    ""ZibalStatus"" INTEGER NULL,
                    ""RefNumber"" INTEGER NULL,
                    ""CardNumber"" TEXT NULL,
                    ""PaidAmount"" TEXT NULL,
                    ""Note"" TEXT NULL,
                    ""InitiatedByUserId"" TEXT NULL,
                    ""PaymentId"" INTEGER NULL,
                    CONSTRAINT ""FK_PaymentAttempts_Bills_BillId"" FOREIGN KEY (""BillId"") REFERENCES ""Bills"" (""Id"") ON DELETE RESTRICT,
                    CONSTRAINT ""FK_PaymentAttempts_Payments_PaymentId"" FOREIGN KEY (""PaymentId"") REFERENCES ""Payments"" (""Id"") ON DELETE RESTRICT
                );
                CREATE UNIQUE INDEX ""IX_PaymentAttempts_PublicId"" ON ""PaymentAttempts"" (""PublicId"");
                CREATE UNIQUE INDEX ""IX_PaymentAttempts_OrderId"" ON ""PaymentAttempts"" (""OrderId"");
                CREATE UNIQUE INDEX ""IX_PaymentAttempts_TrackId"" ON ""PaymentAttempts"" (""TrackId"") WHERE ""TrackId"" IS NOT NULL;
                CREATE INDEX ""IX_PaymentAttempts_BillId"" ON ""PaymentAttempts"" (""BillId"");
                CREATE INDEX ""IX_PaymentAttempts_Status"" ON ""PaymentAttempts"" (""Status"");
                CREATE INDEX ""IX_PaymentAttempts_PaymentId"" ON ""PaymentAttempts"" (""PaymentId"");
            ");

            migrationBuilder.Sql(@"
                CREATE TABLE ""PaymentAttemptEvents"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_PaymentAttemptEvents"" PRIMARY KEY AUTOINCREMENT,
                    ""PaymentAttemptId"" INTEGER NOT NULL,
                    ""CreatedAt"" TEXT NOT NULL,
                    ""EventType"" INTEGER NOT NULL,
                    ""ResultCode"" INTEGER NULL,
                    ""ZibalStatus"" INTEGER NULL,
                    ""Message"" TEXT NULL,
                    ""Data"" TEXT NULL,
                    CONSTRAINT ""FK_PaymentAttemptEvents_PaymentAttempts_PaymentAttemptId"" FOREIGN KEY (""PaymentAttemptId"") REFERENCES ""PaymentAttempts"" (""Id"") ON DELETE CASCADE
                );
                CREATE INDEX ""IX_PaymentAttemptEvents_PaymentAttemptId"" ON ""PaymentAttemptEvents"" (""PaymentAttemptId"");
            ");
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            migrationBuilder.Sql("DROP TABLE [PaymentAttemptEvents];");
            migrationBuilder.Sql("DROP TABLE [PaymentAttempts];");
        }
        else
        {
            migrationBuilder.Sql(@"DROP TABLE ""PaymentAttemptEvents"";");
            migrationBuilder.Sql(@"DROP TABLE ""PaymentAttempts"";");
        }
    }
}
