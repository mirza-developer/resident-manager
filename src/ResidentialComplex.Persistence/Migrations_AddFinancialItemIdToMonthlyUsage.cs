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
        // SQLite doesn't support AddForeignKey or complex ALTER TABLE,
        // so we rebuild the table using raw SQL that works on both SQLite and SQL Server.

        // 1. Create new table with the FinancialItemId column
        migrationBuilder.Sql(@"
            CREATE TABLE ""MonthlyUsages_new"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_MonthlyUsages"" PRIMARY KEY AUTOINCREMENT,
                ""HouseId"" INTEGER NOT NULL,
                ""FinancialItemId"" INTEGER NOT NULL DEFAULT 0,
                ""Year"" INTEGER NOT NULL,
                ""Month"" INTEGER NOT NULL,
                ""UsageCount"" INTEGER NOT NULL,
                CONSTRAINT ""FK_MonthlyUsages_Houses_HouseId"" FOREIGN KEY (""HouseId"") REFERENCES ""Houses"" (""Id"") ON DELETE CASCADE,
                CONSTRAINT ""FK_MonthlyUsages_FinancialItems_FinancialItemId"" FOREIGN KEY (""FinancialItemId"") REFERENCES ""FinancialItems"" (""Id"") ON DELETE CASCADE
            );
        ");

        // 2. Copy existing data (FinancialItemId defaults to 0 for old rows)
        migrationBuilder.Sql(@"
            INSERT INTO ""MonthlyUsages_new"" (""Id"", ""HouseId"", ""FinancialItemId"", ""Year"", ""Month"", ""UsageCount"")
            SELECT ""Id"", ""HouseId"", 0, ""Year"", ""Month"", ""UsageCount""
            FROM ""MonthlyUsages"";
        ");

        // 3. Drop old table
        migrationBuilder.Sql(@"DROP TABLE ""MonthlyUsages"";");

        // 4. Rename new table
        migrationBuilder.Sql(@"ALTER TABLE ""MonthlyUsages_new"" RENAME TO ""MonthlyUsages"";");

        // 5. Recreate the unique index with the new composite key
        migrationBuilder.Sql(@"
            CREATE UNIQUE INDEX ""IX_MonthlyUsages_HouseId_FinancialItemId_Year_Month""
            ON ""MonthlyUsages"" (""HouseId"", ""FinancialItemId"", ""Year"", ""Month"");
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Reverse: rebuild without FinancialItemId
        migrationBuilder.Sql(@"
            CREATE TABLE ""MonthlyUsages_old"" (
                ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_MonthlyUsages"" PRIMARY KEY AUTOINCREMENT,
                ""HouseId"" INTEGER NOT NULL,
                ""Year"" INTEGER NOT NULL,
                ""Month"" INTEGER NOT NULL,
                ""UsageCount"" INTEGER NOT NULL,
                CONSTRAINT ""FK_MonthlyUsages_Houses_HouseId"" FOREIGN KEY (""HouseId"") REFERENCES ""Houses"" (""Id"") ON DELETE CASCADE
            );
        ");

        migrationBuilder.Sql(@"
            INSERT INTO ""MonthlyUsages_old"" (""Id"", ""HouseId"", ""Year"", ""Month"", ""UsageCount"")
            SELECT ""Id"", ""HouseId"", ""Year"", ""Month"", ""UsageCount""
            FROM ""MonthlyUsages"";
        ");

        migrationBuilder.Sql(@"DROP TABLE ""MonthlyUsages"";");
        migrationBuilder.Sql(@"ALTER TABLE ""MonthlyUsages_old"" RENAME TO ""MonthlyUsages"";");

        migrationBuilder.Sql(@"
            CREATE UNIQUE INDEX ""IX_MonthlyUsages_HouseId_Year_Month""
            ON ""MonthlyUsages"" (""HouseId"", ""Year"", ""Month"");
        ");
    }
}
