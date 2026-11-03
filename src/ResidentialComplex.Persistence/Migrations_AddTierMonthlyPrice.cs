using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ResidentialComplex.Persistence.Migrations;

/// <summary>Adds FinancialItemTiers.MonthlyPrice (flat price per 30-day month, default 0).</summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20250101000011_AddTierMonthlyPrice")]
public class AddTierMonthlyPrice : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
            migrationBuilder.Sql(@"ALTER TABLE [FinancialItemTiers] ADD [MonthlyPrice] DECIMAL(18,4) NOT NULL CONSTRAINT [DF_FinancialItemTiers_MonthlyPrice] DEFAULT 0;");
        else
            migrationBuilder.Sql(@"ALTER TABLE ""FinancialItemTiers"" ADD COLUMN ""MonthlyPrice"" TEXT NOT NULL DEFAULT '0';");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            migrationBuilder.Sql(@"ALTER TABLE [FinancialItemTiers] DROP CONSTRAINT [DF_FinancialItemTiers_MonthlyPrice];");
            migrationBuilder.Sql(@"ALTER TABLE [FinancialItemTiers] DROP COLUMN [MonthlyPrice];");
        }
        else
            migrationBuilder.Sql(@"ALTER TABLE ""FinancialItemTiers"" DROP COLUMN ""MonthlyPrice"";");
    }
}
