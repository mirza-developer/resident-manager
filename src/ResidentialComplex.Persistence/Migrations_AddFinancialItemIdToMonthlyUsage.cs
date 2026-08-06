using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ResidentialComplex.Persistence.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20250101000002_AddFinancialItemIdToMonthlyUsage")]
public class AddFinancialItemIdToMonthlyUsage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // For existing databases that ran InitialCreate without FinancialItemId:
        // SQLite doesn't support ALTER TABLE ADD FOREIGN KEY, so we rebuild the table.
        // Any existing MonthlyUsage rows are invalid (no FinancialItemId) and must be cleared.

        // 1. Delete all existing usage rows — they lack a valid FinancialItemId
        migrationBuilder.Sql(@"DELETE FROM ""MonthlyUsages"";");

        // 2. Drop the old table
        migrationBuilder.Sql(@"DROP TABLE ""MonthlyUsages"";");

        // 3. Recreate with the FinancialItemId column and FK constraints
        migrationBuilder.Sql(@"
            CREATE TABLE ""MonthlyUsages"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_MonthlyUsages"" PRIMARY KEY AUTOINCREMENT,
                ""HouseId"" INTEGER NOT NULL,
                ""FinancialItemId"" INTEGER NOT NULL,
                ""Year"" INTEGER NOT NULL,
                ""Month"" INTEGER NOT NULL,
                ""UsageCount"" INTEGER NOT NULL,
                CONSTRAINT ""FK_MonthlyUsages_Houses_HouseId"" FOREIGN KEY (""HouseId"") REFERENCES ""Houses"" (""Id"") ON DELETE CASCADE,
                CONSTRAINT ""FK_MonthlyUsages_FinancialItems_FinancialItemId"" FOREIGN KEY (""FinancialItemId"") REFERENCES ""FinancialItems"" (""Id"") ON DELETE CASCADE
            );
        ");

        // 4. Recreate the unique index
        migrationBuilder.Sql(@"
            CREATE UNIQUE INDEX ""IX_MonthlyUsages_HouseId_FinancialItemId_Year_Month""
            ON ""MonthlyUsages"" (""HouseId"", ""FinancialItemId"", ""Year"", ""Month"");
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"DELETE FROM ""MonthlyUsages"";");
        migrationBuilder.Sql(@"DROP TABLE ""MonthlyUsages"";");

        migrationBuilder.Sql(@"
            CREATE TABLE ""MonthlyUsages"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_MonthlyUsages"" PRIMARY KEY AUTOINCREMENT,
                ""HouseId"" INTEGER NOT NULL,
                ""Year"" INTEGER NOT NULL,
                ""Month"" INTEGER NOT NULL,
                ""UsageCount"" INTEGER NOT NULL,
                CONSTRAINT ""FK_MonthlyUsages_Houses_HouseId"" FOREIGN KEY (""HouseId"") REFERENCES ""Houses"" (""Id"") ON DELETE CASCADE
            );
        ");

        migrationBuilder.Sql(@"
            CREATE UNIQUE INDEX ""IX_MonthlyUsages_HouseId_Year_Month""
            ON ""MonthlyUsages"" (""HouseId"", ""Year"", ""Month"");
        ");
    }
}
