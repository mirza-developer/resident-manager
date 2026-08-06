using Microsoft.EntityFrameworkCore.Migrations;

namespace ResidentialComplex.Persistence.Migrations;

[Migration("20250101000002_AddFinancialItemIdToMonthlyUsage")]
public class AddFinancialItemIdToMonthlyUsage : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Add FinancialItemId column with default value 0 for existing rows
        migrationBuilder.AddColumn<int>(
            name: "FinancialItemId",
            table: "MonthlyUsages",
            nullable: false,
            defaultValue: 0);

        // Drop old unique index
        migrationBuilder.DropIndex(
            name: "IX_MonthlyUsages_HouseId_Year_Month",
            table: "MonthlyUsages");

        // Create new unique index including FinancialItemId
        migrationBuilder.CreateIndex(
            name: "IX_MonthlyUsages_HouseId_FinancialItemId_Year_Month",
            table: "MonthlyUsages",
            columns: new[] { "HouseId", "FinancialItemId", "Year", "Month" },
            unique: true);

        // Add foreign key to FinancialItems
        migrationBuilder.AddForeignKey(
            name: "FK_MonthlyUsages_FinancialItems_FinancialItemId",
            table: "MonthlyUsages",
            column: "FinancialItemId",
            principalTable: "FinancialItems",
            principalColumn: "Id",
            onDelete: ReferentialAction.Cascade);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_MonthlyUsages_FinancialItems_FinancialItemId",
            table: "MonthlyUsages");

        migrationBuilder.DropIndex(
            name: "IX_MonthlyUsages_HouseId_FinancialItemId_Year_Month",
            table: "MonthlyUsages");

        migrationBuilder.CreateIndex(
            name: "IX_MonthlyUsages_HouseId_Year_Month",
            table: "MonthlyUsages",
            columns: new[] { "HouseId", "Year", "Month" },
            unique: true);

        migrationBuilder.DropColumn(
            name: "FinancialItemId",
            table: "MonthlyUsages");
    }
}
