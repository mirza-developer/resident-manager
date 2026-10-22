using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ResidentialComplex.Persistence.Migrations;

/// <summary>
/// Adds the AuthorityBills table: the bulk bill received from the authority for a financial item
/// of type AuthorityBill, one per (FinancialItemId, Year, Month).
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20250101000008_AddAuthorityBills")]
public class AddAuthorityBills : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            migrationBuilder.Sql(@"
                CREATE TABLE [AuthorityBills] (
                    [Id] INT NOT NULL IDENTITY(1,1) CONSTRAINT [PK_AuthorityBills] PRIMARY KEY,
                    [FinancialItemId] INT NOT NULL,
                    [Year] INT NOT NULL,
                    [Month] INT NOT NULL,
                    [PeriodDays] INT NOT NULL,
                    [TotalUse] DECIMAL(18,2) NOT NULL,
                    [TotalPrice] DECIMAL(18,2) NOT NULL,
                    [Tax] DECIMAL(18,2) NOT NULL,
                    [Notes] NVARCHAR(500) NULL,
                    CONSTRAINT [FK_AuthorityBills_FinancialItems_FinancialItemId] FOREIGN KEY ([FinancialItemId]) REFERENCES [FinancialItems] ([Id]) ON DELETE CASCADE
                );
            ");
            migrationBuilder.Sql(@"CREATE UNIQUE INDEX [IX_AuthorityBills_FinancialItemId_Year_Month] ON [AuthorityBills] ([FinancialItemId], [Year], [Month]);");
        }
        else
        {
            // SQLite
            migrationBuilder.Sql(@"
                CREATE TABLE ""AuthorityBills"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_AuthorityBills"" PRIMARY KEY AUTOINCREMENT,
                    ""FinancialItemId"" INTEGER NOT NULL,
                    ""Year"" INTEGER NOT NULL,
                    ""Month"" INTEGER NOT NULL,
                    ""PeriodDays"" INTEGER NOT NULL,
                    ""TotalUse"" decimal(18,2) NOT NULL,
                    ""TotalPrice"" decimal(18,2) NOT NULL,
                    ""Tax"" decimal(18,2) NOT NULL,
                    ""Notes"" TEXT NULL,
                    CONSTRAINT ""FK_AuthorityBills_FinancialItems_FinancialItemId"" FOREIGN KEY (""FinancialItemId"") REFERENCES ""FinancialItems"" (""Id"") ON DELETE CASCADE
                );
            ");
            migrationBuilder.Sql(@"CREATE UNIQUE INDEX ""IX_AuthorityBills_FinancialItemId_Year_Month"" ON ""AuthorityBills"" (""FinancialItemId"", ""Year"", ""Month"");");
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            migrationBuilder.Sql(@"DROP TABLE [AuthorityBills];");
        else
            migrationBuilder.Sql(@"DROP TABLE ""AuthorityBills"";");
    }
}
