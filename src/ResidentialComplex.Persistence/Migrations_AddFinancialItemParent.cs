using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ResidentialComplex.Persistence.Migrations;

/// <summary>
/// Adds FinancialItems.ParentId so a parent item (e.g. water, CalculationType 2 = WaterParent)
/// can own hot, cold and shared-consumption child items. Existing type-2 items become parents.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20250101000009_AddFinancialItemParent")]
public class AddFinancialItemParent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            migrationBuilder.Sql(@"ALTER TABLE [FinancialItems] ADD [ParentId] INT NULL;");
            migrationBuilder.Sql(@"ALTER TABLE [FinancialItems] ADD [WaterKind] INT NOT NULL CONSTRAINT [DF_FinancialItems_WaterKind] DEFAULT 0;");
            migrationBuilder.Sql(@"CREATE INDEX [IX_FinancialItems_ParentId] ON [FinancialItems] ([ParentId]);");
            migrationBuilder.Sql(@"ALTER TABLE [FinancialItems] ADD CONSTRAINT [FK_FinancialItems_FinancialItems_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [FinancialItems] ([Id]);");
        }
        else
        {
            // SQLite: ADD COLUMN with an inline REFERENCES clause is supported.
            migrationBuilder.Sql(@"ALTER TABLE ""FinancialItems"" ADD COLUMN ""ParentId"" INTEGER NULL REFERENCES ""FinancialItems"" (""Id"");");
            migrationBuilder.Sql(@"ALTER TABLE ""FinancialItems"" ADD COLUMN ""WaterKind"" INTEGER NOT NULL DEFAULT 0;");
            migrationBuilder.Sql(@"CREATE INDEX ""IX_FinancialItems_ParentId"" ON ""FinancialItems"" (""ParentId"");");
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            migrationBuilder.Sql(@"ALTER TABLE [FinancialItems] DROP CONSTRAINT [FK_FinancialItems_FinancialItems_ParentId];");
            migrationBuilder.Sql(@"DROP INDEX [IX_FinancialItems_ParentId] ON [FinancialItems];");
            migrationBuilder.Sql(@"ALTER TABLE [FinancialItems] DROP COLUMN [ParentId];");
            migrationBuilder.Sql(@"ALTER TABLE [FinancialItems] DROP CONSTRAINT [DF_FinancialItems_WaterKind];");
            migrationBuilder.Sql(@"ALTER TABLE [FinancialItems] DROP COLUMN [WaterKind];");
        }
        else
        {
            migrationBuilder.Sql(@"DROP INDEX ""IX_FinancialItems_ParentId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""FinancialItems"" DROP COLUMN ""ParentId"";");
            migrationBuilder.Sql(@"ALTER TABLE ""FinancialItems"" DROP COLUMN ""WaterKind"";");
        }
    }
}
