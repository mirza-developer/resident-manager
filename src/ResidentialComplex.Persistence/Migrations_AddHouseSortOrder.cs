using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace ResidentialComplex.Persistence.Migrations;

/// <summary>
/// Adds Houses.SortOrder, the manual display order used by every list of houses.
/// Existing rows are initialised from the title: "واحد N" (N = digits, Latin/Persian/Arabic-Indic) → N,
/// anything else → 0. This mirrors House.DefaultSortOrderFromTitle.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20250101000012_AddHouseSortOrder")]
public class AddHouseSortOrder : Migration
{
    private const string Word = "\u0648\u0627\u062D\u062F"; // واحد

    /// <summary>Wraps a column in REPLACE() calls turning Persian and Arabic-Indic digits into 0-9.</summary>
    private static string NormalizeDigits(string column, bool sqlServer)
    {
        var expr = column;
        for (var i = 0; i < 10; i++)
        {
            var persian = ((char)('\u06F0' + i)).ToString();
            var arabic = ((char)('\u0660' + i)).ToString();
            var latin = i.ToString();
            expr = sqlServer
                ? $"REPLACE(REPLACE({expr}, N'{persian}', N'{latin}'), N'{arabic}', N'{latin}')"
                : $"REPLACE(REPLACE({expr}, '{persian}', '{latin}'), '{arabic}', '{latin}')";
        }

        return expr;
    }

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            migrationBuilder.Sql(@"ALTER TABLE [Houses] ADD [SortOrder] INT NOT NULL CONSTRAINT [DF_Houses_SortOrder] DEFAULT 0;");

            // Whole title must be "واحد", a space, then 1-9 digits.
            migrationBuilder.Sql($@"
                UPDATE h
                SET h.[SortOrder] = CAST(LTRIM(SUBSTRING(d.[T], 5, 4000)) AS INT)
                FROM [Houses] h
                INNER JOIN (SELECT [Id], LTRIM(RTRIM({NormalizeDigits("[Title]", true)})) AS [T] FROM [Houses]) d ON d.[Id] = h.[Id]
                WHERE LEFT(d.[T], 4) = N'{Word}'
                  AND SUBSTRING(d.[T], 5, 1) = N' '
                  AND LTRIM(SUBSTRING(d.[T], 5, 4000)) NOT LIKE N'%[^0-9]%'
                  AND LEN(LTRIM(SUBSTRING(d.[T], 5, 4000))) BETWEEN 1 AND 9;
            ");
        }
        else
        {
            // SQLite (used in tests / development)
            migrationBuilder.Sql(@"ALTER TABLE ""Houses"" ADD COLUMN ""SortOrder"" INTEGER NOT NULL DEFAULT 0;");

            migrationBuilder.Sql($@"
                UPDATE ""Houses""
                SET ""SortOrder"" = CAST(TRIM(substr(d.T, 5)) AS INTEGER)
                FROM (SELECT ""Id"" AS Id, TRIM({NormalizeDigits("\"Title\"", false)}) AS T FROM ""Houses"") d
                WHERE ""Houses"".""Id"" = d.Id
                  AND substr(d.T, 1, 4) = '{Word}'
                  AND substr(d.T, 5, 1) = ' '
                  AND TRIM(substr(d.T, 5)) NOT GLOB '*[^0-9]*'
                  AND length(TRIM(substr(d.T, 5))) BETWEEN 1 AND 9;
            ");
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        if (migrationBuilder.ActiveProvider == "Microsoft.EntityFrameworkCore.SqlServer")
        {
            migrationBuilder.Sql(@"ALTER TABLE [Houses] DROP CONSTRAINT [DF_Houses_SortOrder];");
            migrationBuilder.Sql(@"ALTER TABLE [Houses] DROP COLUMN [SortOrder];");
        }
        else
        {
            migrationBuilder.Sql(@"ALTER TABLE ""Houses"" DROP COLUMN ""SortOrder"";");
        }
    }
}
