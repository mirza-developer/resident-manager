using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ResidentialComplex.Persistence.Migrations;

/// <summary>
/// Creates the SmsTemplates table and seeds the "BillApproved" template with the exact
/// wording that used to be hardcoded in BillingService.ApproveBillsAsync, now expressed
/// with placeholder tokens ({PeriodTitle}, {TotalAmount}, etc.) that admins can edit from
/// the new "قالب‌های پیامک" (SMS Templates) admin page.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20250101000005_AddSmsTemplates")]
public class AddSmsTemplates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            migrationBuilder.Sql(@"
                CREATE TABLE [SmsTemplates] (
                    [Id] INT NOT NULL IDENTITY(1, 1) CONSTRAINT [PK_SmsTemplates] PRIMARY KEY,
                    [Key] NVARCHAR(100) NOT NULL,
                    [Title] NVARCHAR(200) NOT NULL,
                    [Text] NVARCHAR(2000) NOT NULL,
                    [UpdatedAtUtc] DATETIME2 NULL,
                    [RowVersion] BIGINT NOT NULL
                );
                CREATE UNIQUE INDEX [IX_SmsTemplates_Key] ON [SmsTemplates] ([Key]);
            ");

            migrationBuilder.Sql(@"
                INSERT INTO [SmsTemplates] ([Key], [Title], [Text], [UpdatedAtUtc], [RowVersion])
                VALUES (
                    N'BillApproved',
                    N'پیامک صدور قبض',
                    N'مالک محترم
قبض شارژ {PeriodTitle}
صادر شد.
مبلغ قابل پرداخت {TotalAmount} تومان
لطفا در اسرع وقت اقدام به پرداخت نمایید',
                    NULL,
                    0
                );
            ");
        }
        else
        {
            // SQLite (used in tests / development)
            migrationBuilder.Sql(@"
                CREATE TABLE ""SmsTemplates"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_SmsTemplates"" PRIMARY KEY AUTOINCREMENT,
                    ""Key"" TEXT NOT NULL,
                    ""Title"" TEXT NOT NULL,
                    ""Text"" TEXT NOT NULL,
                    ""UpdatedAtUtc"" TEXT NULL,
                    ""RowVersion"" INTEGER NOT NULL
                );
                CREATE UNIQUE INDEX ""IX_SmsTemplates_Key"" ON ""SmsTemplates"" (""Key"");
            ");

            migrationBuilder.Sql(@"
                INSERT INTO ""SmsTemplates"" (""Key"", ""Title"", ""Text"", ""UpdatedAtUtc"", ""RowVersion"")
                VALUES (
                    'BillApproved',
                    'پیامک صدور قبض',
                    'مالک محترم
قبض شارژ {PeriodTitle}
صادر شد.
مبلغ قابل پرداخت {TotalAmount} تومان
لطفا در اسرع وقت اقدام به پرداخت نمایید',
                    NULL,
                    0
                );
            ");
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("SmsTemplates");
    }
}
