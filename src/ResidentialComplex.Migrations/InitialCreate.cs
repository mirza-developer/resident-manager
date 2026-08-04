using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ResidentialComplex.Persistence;

namespace ResidentialComplex.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20250101000001_InitialCreate")]
public class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ASP.NET Identity tables
        migrationBuilder.CreateTable(
            name: "AspNetRoles",
            columns: table => new
            {
                Id = table.Column<string>(maxLength: 450, nullable: false),
                Name = table.Column<string>(maxLength: 256, nullable: true),
                NormalizedName = table.Column<string>(maxLength: 256, nullable: true),
                ConcurrencyStamp = table.Column<string>(nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_AspNetRoles", x => x.Id));

        migrationBuilder.CreateTable(
            name: "AspNetUsers",
            columns: table => new
            {
                Id = table.Column<string>(maxLength: 450, nullable: false),
                FullName = table.Column<string>(maxLength: 200, nullable: false, defaultValue: ""),
                UserName = table.Column<string>(maxLength: 256, nullable: true),
                NormalizedUserName = table.Column<string>(maxLength: 256, nullable: true),
                Email = table.Column<string>(maxLength: 256, nullable: true),
                NormalizedEmail = table.Column<string>(maxLength: 256, nullable: true),
                EmailConfirmed = table.Column<bool>(nullable: false),
                PasswordHash = table.Column<string>(nullable: true),
                SecurityStamp = table.Column<string>(nullable: true),
                ConcurrencyStamp = table.Column<string>(nullable: true),
                PhoneNumber = table.Column<string>(nullable: true),
                PhoneNumberConfirmed = table.Column<bool>(nullable: false),
                TwoFactorEnabled = table.Column<bool>(nullable: false),
                LockoutEnd = table.Column<DateTimeOffset>(nullable: true),
                LockoutEnabled = table.Column<bool>(nullable: false),
                AccessFailedCount = table.Column<int>(nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_AspNetUsers", x => x.Id));

        migrationBuilder.CreateTable(
            name: "AspNetRoleClaims",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                RoleId = table.Column<string>(maxLength: 450, nullable: false),
                ClaimType = table.Column<string>(nullable: true),
                ClaimValue = table.Column<string>(nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                table.ForeignKey("FK_AspNetRoleClaims_AspNetRoles_RoleId", x => x.RoleId, "AspNetRoles", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUserClaims",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<string>(maxLength: 450, nullable: false),
                ClaimType = table.Column<string>(nullable: true),
                ClaimValue = table.Column<string>(nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                table.ForeignKey("FK_AspNetUserClaims_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUserLogins",
            columns: table => new
            {
                LoginProvider = table.Column<string>(maxLength: 128, nullable: false),
                ProviderKey = table.Column<string>(maxLength: 128, nullable: false),
                ProviderDisplayName = table.Column<string>(nullable: true),
                UserId = table.Column<string>(maxLength: 450, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                table.ForeignKey("FK_AspNetUserLogins_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUserRoles",
            columns: table => new
            {
                UserId = table.Column<string>(maxLength: 450, nullable: false),
                RoleId = table.Column<string>(maxLength: 450, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                table.ForeignKey("FK_AspNetUserRoles_AspNetRoles_RoleId", x => x.RoleId, "AspNetRoles", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_AspNetUserRoles_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "AspNetUserTokens",
            columns: table => new
            {
                UserId = table.Column<string>(maxLength: 450, nullable: false),
                LoginProvider = table.Column<string>(maxLength: 128, nullable: false),
                Name = table.Column<string>(maxLength: 128, nullable: false),
                Value = table.Column<string>(nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                table.ForeignKey("FK_AspNetUserTokens_AspNetUsers_UserId", x => x.UserId, "AspNetUsers", "Id", onDelete: ReferentialAction.Cascade);
            });

        // Domain tables
        migrationBuilder.CreateTable(
            name: "Apartments",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Title = table.Column<string>(maxLength: 200, nullable: false),
                Description = table.Column<string>(maxLength: 500, nullable: true),
                RowVersion = table.Column<long>(nullable: false, defaultValue: 0L)
            },
            constraints: table => table.PrimaryKey("PK_Apartments", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Houses",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Title = table.Column<string>(maxLength: 200, nullable: false),
                ResidentName = table.Column<string>(maxLength: 200, nullable: false),
                ResidentPhoneNumber = table.Column<string>(maxLength: 20, nullable: false),
                NumberOfResidents = table.Column<int>(nullable: false),
                CurrentDebt = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                IsActive = table.Column<bool>(nullable: false, defaultValue: true),
                RowVersion = table.Column<long>(nullable: false, defaultValue: 0L),
                ApartmentId = table.Column<int>(nullable: false),
                ApplicationUserId = table.Column<string>(maxLength: 450, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Houses", x => x.Id);
                table.ForeignKey("FK_Houses_Apartments_ApartmentId", x => x.ApartmentId, "Apartments", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "FinancialItems",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Title = table.Column<string>(maxLength: 200, nullable: false),
                Description = table.Column<string>(maxLength: 500, nullable: true),
                PeriodType = table.Column<int>(nullable: false),
                CalculationType = table.Column<int>(nullable: false),
                IsActive = table.Column<bool>(nullable: false, defaultValue: true),
                TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                NumberOfInstallments = table.Column<int>(nullable: true),
                InstallmentsBilled = table.Column<int>(nullable: false, defaultValue: 0),
                NumberOfGroups = table.Column<int>(nullable: true),
                RowVersion = table.Column<long>(nullable: false, defaultValue: 0L)
            },
            constraints: table => table.PrimaryKey("PK_FinancialItems", x => x.Id));

        migrationBuilder.CreateTable(
            name: "FinancialItemGroupPoints",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                FinancialItemId = table.Column<int>(nullable: false),
                GroupNumber = table.Column<int>(nullable: false),
                PointValue = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FinancialItemGroupPoints", x => x.Id);
                table.ForeignKey("FK_FinancialItemGroupPoints_FinancialItems", x => x.FinancialItemId, "FinancialItems", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "MonthlyUsages",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                HouseId = table.Column<int>(nullable: false),
                Year = table.Column<int>(nullable: false),
                Month = table.Column<int>(nullable: false),
                UsageCount = table.Column<int>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MonthlyUsages", x => x.Id);
                table.ForeignKey("FK_MonthlyUsages_Houses_HouseId", x => x.HouseId, "Houses", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_MonthlyUsages_HouseId_Year_Month", "MonthlyUsages", new[] { "HouseId", "Year", "Month" }, unique: true);

        migrationBuilder.CreateTable(
            name: "Bills",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                HouseId = table.Column<int>(nullable: false),
                Year = table.Column<int>(nullable: false),
                Month = table.Column<int>(nullable: false),
                TotalAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Description = table.Column<string>(maxLength: 500, nullable: true),
                Status = table.Column<int>(nullable: false),
                CreatedDate = table.Column<DateTime>(nullable: false),
                ApprovedDate = table.Column<DateTime>(nullable: true),
                PaidDate = table.Column<DateTime>(nullable: true),
                RowVersion = table.Column<long>(nullable: false, defaultValue: 0L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Bills", x => x.Id);
                table.ForeignKey("FK_Bills_Houses_HouseId", x => x.HouseId, "Houses", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_Bills_HouseId_Year_Month", "Bills", new[] { "HouseId", "Year", "Month" }, unique: true);

        migrationBuilder.CreateTable(
            name: "BillItems",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                BillId = table.Column<int>(nullable: false),
                FinancialItemId = table.Column<int>(nullable: false),
                Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                Description = table.Column<string>(maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_BillItems", x => x.Id);
                table.ForeignKey("FK_BillItems_Bills_BillId", x => x.BillId, "Bills", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_BillItems_FinancialItems", x => x.FinancialItemId, "FinancialItems", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "Payments",
            columns: table => new
            {
                Id = table.Column<int>(nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                BillId = table.Column<int>(nullable: false),
                Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                PaymentDate = table.Column<DateTime>(nullable: false),
                Description = table.Column<string>(maxLength: 500, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Payments", x => x.Id);
                table.ForeignKey("FK_Payments_Bills_BillId", x => x.BillId, "Bills", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "AuditLogs",
            columns: table => new
            {
                Id = table.Column<long>(nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                UserId = table.Column<string>(maxLength: 450, nullable: false),
                UserName = table.Column<string>(maxLength: 256, nullable: false),
                DateTime = table.Column<DateTime>(nullable: false),
                EntityName = table.Column<string>(maxLength: 100, nullable: false),
                EntityId = table.Column<string>(maxLength: 100, nullable: false),
                Action = table.Column<string>(maxLength: 100, nullable: false),
                OldValues = table.Column<string>(nullable: true),
                NewValues = table.Column<string>(nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_AuditLogs", x => x.Id));

        // Identity indexes
        migrationBuilder.CreateIndex("IX_AspNetRoleClaims_RoleId", "AspNetRoleClaims", "RoleId");
        migrationBuilder.CreateIndex("RoleNameIndex", "AspNetRoles", "NormalizedName", unique: true, filter: null);
        migrationBuilder.CreateIndex("IX_AspNetUserClaims_UserId", "AspNetUserClaims", "UserId");
        migrationBuilder.CreateIndex("IX_AspNetUserLogins_UserId", "AspNetUserLogins", "UserId");
        migrationBuilder.CreateIndex("IX_AspNetUserRoles_RoleId", "AspNetUserRoles", "RoleId");
        migrationBuilder.CreateIndex("EmailIndex", "AspNetUsers", "NormalizedEmail");
        migrationBuilder.CreateIndex("UserNameIndex", "AspNetUsers", "NormalizedUserName", unique: true, filter: null);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("AuditLogs");
        migrationBuilder.DropTable("Payments");
        migrationBuilder.DropTable("BillItems");
        migrationBuilder.DropTable("Bills");
        migrationBuilder.DropTable("MonthlyUsages");
        migrationBuilder.DropTable("FinancialItemGroupPoints");
        migrationBuilder.DropTable("FinancialItems");
        migrationBuilder.DropTable("Houses");
        migrationBuilder.DropTable("Apartments");
        migrationBuilder.DropTable("AspNetRoleClaims");
        migrationBuilder.DropTable("AspNetUserClaims");
        migrationBuilder.DropTable("AspNetUserLogins");
        migrationBuilder.DropTable("AspNetUserRoles");
        migrationBuilder.DropTable("AspNetUserTokens");
        migrationBuilder.DropTable("AspNetRoles");
        migrationBuilder.DropTable("AspNetUsers");
    }
}
