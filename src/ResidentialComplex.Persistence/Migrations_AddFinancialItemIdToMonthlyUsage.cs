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
        if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            // SQL Server: simply add the column and FK (FinancialItemId already exists from InitialCreate
            // in new databases, but for databases that ran an older InitialCreate without it):
            migrationBuilder.Sql("DELETE FROM [MonthlyUsages];");
            migrationBuilder.Sql("DROP TABLE [MonthlyUsages];");

            migrationBuilder.Sql(@"
                CREATE TABLE [MonthlyUsages] (
                    [Id] INT NOT NULL IDENTITY(1, 1) CONSTRAINT [PK_MonthlyUsages] PRIMARY KEY,
                    [HouseId] INT NOT NULL,
                    [FinancialItemId] INT NOT NULL,
                    [Year] INT NOT NULL,
                    [Month] INT NOT NULL,
                    [UsageCount] INT NOT NULL,
                    CONSTRAINT [FK_MonthlyUsages_Houses_HouseId] FOREIGN KEY ([HouseId]) REFERENCES [Houses] ([Id]) ON DELETE CASCADE,
                    CONSTRAINT [FK_MonthlyUsages_FinancialItems_FinancialItemId] FOREIGN KEY ([FinancialItemId]) REFERENCES [FinancialItems] ([Id]) ON DELETE CASCADE
                );
            ");

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX [IX_MonthlyUsages_HouseId_FinancialItemId_Year_Month]
                ON [MonthlyUsages] ([HouseId], [FinancialItemId], [Year], [Month]);
            ");
        }
        else
        {
            // SQLite: rebuild the table (ALTER TABLE limitations)
            migrationBuilder.Sql(@"DELETE FROM ""MonthlyUsages"";");
            migrationBuilder.Sql(@"DROP TABLE ""MonthlyUsages"";");

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

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX ""IX_MonthlyUsages_HouseId_FinancialItemId_Year_Month""
                ON ""MonthlyUsages"" (""HouseId"", ""FinancialItemId"", ""Year"", ""Month"");
            ");
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            migrationBuilder.Sql("DELETE FROM [MonthlyUsages];");
            migrationBuilder.Sql("DROP TABLE [MonthlyUsages];");

            migrationBuilder.Sql(@"
                CREATE TABLE [MonthlyUsages] (
                    [Id] INT NOT NULL IDENTITY(1, 1) CONSTRAINT [PK_MonthlyUsages] PRIMARY KEY,
                    [HouseId] INT NOT NULL,
                    [Year] INT NOT NULL,
                    [Month] INT NOT NULL,
                    [UsageCount] INT NOT NULL,
                    CONSTRAINT [FK_MonthlyUsages_Houses_HouseId] FOREIGN KEY ([HouseId]) REFERENCES [Houses] ([Id]) ON DELETE CASCADE
                );
            ");

            migrationBuilder.Sql(@"
                CREATE UNIQUE INDEX [IX_MonthlyUsages_HouseId_Year_Month]
                ON [MonthlyUsages] ([HouseId], [Year], [Month]);
            ");
        }
        else
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
}
