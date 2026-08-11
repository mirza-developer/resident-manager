using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ResidentialComplex.Persistence.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20250101000003_AddFinancialItemTiers")]
public class AddFinancialItemTiers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            migrationBuilder.Sql(@"
                CREATE TABLE [FinancialItemTiers] (
                    [Id] INT NOT NULL IDENTITY(1, 1) CONSTRAINT [PK_FinancialItemTiers] PRIMARY KEY,
                    [FinancialItemId] INT NOT NULL,
                    [TierOrder] INT NOT NULL,
                    [UpperLimit] INT NULL,
                    [RatePerUnit] DECIMAL(18,4) NOT NULL,
                    CONSTRAINT [FK_FinancialItemTiers_FinancialItems] FOREIGN KEY ([FinancialItemId]) REFERENCES [FinancialItems] ([Id]) ON DELETE CASCADE
                );
                CREATE INDEX [IX_FinancialItemTiers_FinancialItemId] ON [FinancialItemTiers] ([FinancialItemId]);
            ");
        }
        else
        {
            // SQLite (used in tests / development)
            migrationBuilder.Sql(@"
                CREATE TABLE ""FinancialItemTiers"" (
                    ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_FinancialItemTiers"" PRIMARY KEY AUTOINCREMENT,
                    ""FinancialItemId"" INTEGER NOT NULL,
                    ""TierOrder"" INTEGER NOT NULL,
                    ""UpperLimit"" INTEGER NULL,
                    ""RatePerUnit"" TEXT NOT NULL,
                    CONSTRAINT ""FK_FinancialItemTiers_FinancialItems"" FOREIGN KEY (""FinancialItemId"") REFERENCES ""FinancialItems"" (""Id"") ON DELETE CASCADE
                );
                CREATE INDEX ""IX_FinancialItemTiers_FinancialItemId"" ON ""FinancialItemTiers"" (""FinancialItemId"");
            ");
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("FinancialItemTiers");
    }
}
