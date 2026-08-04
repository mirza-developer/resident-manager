using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace ResidentialComplex.Persistence.Migrations;

/// <summary>
/// Model snapshot for migrations.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
public class ApplicationDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        // This is intentionally minimal. EF Core uses this to determine
        // the current state of the model for migration diffing.
        // Since we write all migrations manually, the snapshot serves as
        // a reference point.
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0");
    }
}
