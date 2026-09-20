using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ResidentialComplex.Persistence.Migrations;

/// <summary>
/// Adds meter-reading differential columns (PreviousReading / CurrentReading) to
/// MonthlyUsages, replacing the single static UsageCount as the value the worker enters.
/// UsageCount is kept as a persisted column (now derived as CurrentReading - PreviousReading
/// by the application) so billing calculations did not need any schema-level change.
///
/// Existing rows are backfilled with PreviousReading = 0 and CurrentReading = UsageCount,
/// so CurrentReading - PreviousReading still equals the original UsageCount for old data.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20250101000004_AddMeterReadingsToMonthlyUsage")]
public class AddMeterReadingsToMonthlyUsage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            migrationBuilder.Sql(@"
                ALTER TABLE [MonthlyUsages] ADD [PreviousReading] INT NOT NULL CONSTRAINT [DF_MonthlyUsages_PreviousReading] DEFAULT 0;
            ");
            migrationBuilder.Sql(@"
                ALTER TABLE [MonthlyUsages] ADD [CurrentReading] INT NOT NULL CONSTRAINT [DF_MonthlyUsages_CurrentReading] DEFAULT 0;
            ");

            // Backfill: previous reading unknown for historical rows, so start it at 0 and
            // set the current reading to the existing UsageCount, preserving the same
            // consumption figure (CurrentReading - PreviousReading == UsageCount).
            migrationBuilder.Sql(@"UPDATE [MonthlyUsages] SET [CurrentReading] = [UsageCount];");

            migrationBuilder.Sql(@"ALTER TABLE [MonthlyUsages] DROP CONSTRAINT [DF_MonthlyUsages_PreviousReading];");
            migrationBuilder.Sql(@"ALTER TABLE [MonthlyUsages] DROP CONSTRAINT [DF_MonthlyUsages_CurrentReading];");
        }
        else
        {
            // SQLite
            migrationBuilder.Sql(@"ALTER TABLE ""MonthlyUsages"" ADD COLUMN ""PreviousReading"" INTEGER NOT NULL DEFAULT 0;");
            migrationBuilder.Sql(@"ALTER TABLE ""MonthlyUsages"" ADD COLUMN ""CurrentReading"" INTEGER NOT NULL DEFAULT 0;");
            migrationBuilder.Sql(@"UPDATE ""MonthlyUsages"" SET ""CurrentReading"" = ""UsageCount"";");
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            migrationBuilder.Sql(@"ALTER TABLE [MonthlyUsages] DROP COLUMN [PreviousReading];");
            migrationBuilder.Sql(@"ALTER TABLE [MonthlyUsages] DROP COLUMN [CurrentReading];");
        }
        else
        {
            // SQLite has no DROP COLUMN (pre-3.35) — rebuild the table without the new columns.
            migrationBuilder.Sql(@"
                CREATE TABLE ""MonthlyUsages_old"" (
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
            migrationBuilder.Sql(@"
                INSERT INTO ""MonthlyUsages_old"" (""Id"", ""HouseId"", ""FinancialItemId"", ""Year"", ""Month"", ""UsageCount"")
                SELECT ""Id"", ""HouseId"", ""FinancialItemId"", ""Year"", ""Month"", ""UsageCount"" FROM ""MonthlyUsages"";
            ");
            migrationBuilder.Sql(@"DROP TABLE ""MonthlyUsages"";");
            migrationBuilder.Sql(@"ALTER TABLE ""MonthlyUsages_old"" RENAME TO ""MonthlyUsages"";");
            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ""IX_MonthlyUsages_HouseId_FinancialItemId_Year_Month""
                ON ""MonthlyUsages"" (""HouseId"", ""FinancialItemId"", ""Year"", ""Month"");
            ");
        }
    }
}
