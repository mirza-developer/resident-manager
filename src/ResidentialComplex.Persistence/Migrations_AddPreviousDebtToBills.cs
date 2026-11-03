using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ResidentialComplex.Persistence.Migrations;

/// <summary>
/// Adds Bills.PreviousDebt: the house's outstanding debt carried into the bill when it was
/// generated (already included in TotalAmount). Existing bills are backfilled with 0.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20250101000007_AddPreviousDebtToBills")]
public class AddPreviousDebtToBills : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            migrationBuilder.Sql(@"
                ALTER TABLE [Bills] ADD [PreviousDebt] DECIMAL(18,2) NOT NULL CONSTRAINT [DF_Bills_PreviousDebt] DEFAULT 0;
            ");
            migrationBuilder.Sql(@"ALTER TABLE [Bills] DROP CONSTRAINT [DF_Bills_PreviousDebt];");
        }
        else
        {
            // SQLite
            migrationBuilder.Sql(@"ALTER TABLE ""Bills"" ADD COLUMN ""PreviousDebt"" decimal(18,2) NOT NULL DEFAULT 0;");
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            migrationBuilder.Sql(@"ALTER TABLE [Bills] DROP COLUMN [PreviousDebt];");
        }
        else
        {
            // The column has no index or constraint, so SQLite (3.35+) can drop it directly.
            migrationBuilder.Sql(@"ALTER TABLE ""Bills"" DROP COLUMN ""PreviousDebt"";");
        }
    }
}
