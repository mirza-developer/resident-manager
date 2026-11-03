using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ResidentialComplex.Persistence.Migrations;

/// <summary>
/// Collapses the calculation types into EqualDivision (0) and ParentChildTiering (1).
/// Old WaterParent (2) and SharedConsumption (3) rows become type 1; old shared rows get
/// WaterKind = 3 (Shared). Old Grouping (1) rows keep their value; children keep their ParentId.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20250101000010_CollapseCalculationTypes")]
public class CollapseCalculationTypes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            migrationBuilder.Sql(@"UPDATE [FinancialItems] SET [WaterKind] = 3 WHERE [CalculationType] = 3;");
            migrationBuilder.Sql(@"UPDATE [FinancialItems] SET [CalculationType] = 1 WHERE [CalculationType] IN (2, 3);");
        }
        else
        {
            migrationBuilder.Sql(@"UPDATE ""FinancialItems"" SET ""WaterKind"" = 3 WHERE ""CalculationType"" = 3;");
            migrationBuilder.Sql(@"UPDATE ""FinancialItems"" SET ""CalculationType"" = 1 WHERE ""CalculationType"" IN (2, 3);");
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Data collapse is not reversible without losing which rows were parents; intentionally a no-op.
    }
}
