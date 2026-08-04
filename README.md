# Residential Complex Financial Management System

A complete financial management system for residential complexes, built with ASP.NET Core 10 Blazor Server.

## Project Overview

This system manages the financial operations of a residential complex including apartments, houses (units), monthly billing, payments, debt tracking, and financial reporting. The UI is in Persian (Farsi) with RTL layout.

## Business Scenario

A residential complex administrator uses this system to:
1. Define apartments (blocks/buildings) and houses (units)
2. Create financial items with different period and calculation types
3. Generate monthly bills for all active houses
4. Review, approve, and track bill payments
5. Monitor financial health through reports and analytics

Workers enter monthly usage data for grouping-based calculations.
Residents can view their own debt, bills, and payment status.

## Architecture

The project follows **Clean Architecture** with clear separation of concerns:

```
ResidentialComplex.slnx
├── src/
│   ├── ResidentialComplex.Domain          # Entities, Enums (no dependencies)
│   ├── ResidentialComplex.Application     # Services, DTOs, Interfaces (depends on Domain)
│   ├── ResidentialComplex.Persistence     # DbContext, EF Configurations, Repositories, Migrations (depends on Domain, Application)
│   ├── ResidentialComplex.Infrastructure  # Audit service implementation (depends on Application, Persistence)
│   └── ResidentialComplex.Web             # Blazor Server app, UI, Auth (depends on all)
└── tests/
    └── ResidentialComplex.Tests           # Integration tests with SQLite
```

### Key Design Decisions
- **Central Package Management** via `Directory.Packages.props`
- **`.slnx` solution format** (modern XML-based)
- **Manual migrations** (no EF tooling generation)
- **Optimistic concurrency** via `RowVersion` concurrency tokens
- **All financial values use `decimal`** — never `float` or `double`

## Domain Model

### Entities

| Entity | Description |
|--------|-------------|
| `Apartment` | A block/building in the complex |
| `House` | A residential unit within an apartment |
| `FinancialItem` | A billing item with period and calculation type |
| `FinancialItemGroupPoint` | Point values for groups in Grouping calculation |
| `MonthlyUsage` | Monthly usage count per house (for Grouping) |
| `Bill` | Monthly bill for a house |
| `BillItem` | Line item within a bill |
| `Payment` | Payment record for a bill |
| `AuditLog` | Audit trail for financial operations |

### Enums

- **PeriodType**: `Once`, `Permanent`, `Installment`
- **CalculationType**: `EqualDivision`, `Grouping`
- **BillStatus**: `Draft`, `Approved`, `Paid`

## Database Design

### Providers
- **SQL Server** (primary)
- **SQLite** (secondary, used for tests)

Provider is configurable in `appsettings.json`:

```json
{
  "Database": {
    "Provider": "SqlServer",
    "ConnectionString": "Server=.;Database=ResidentialComplex;Trusted_Connection=true;TrustServerCertificate=true"
  }
}
```

Supported `Provider` values: `SqlServer`, `Sqlite`

### Key Constraints
- Unique index on `Bills(HouseId, Year, Month)` — one bill per house per month
- Unique index on `MonthlyUsages(HouseId, Year, Month)` — one usage record per house per month
- Foreign keys with appropriate delete behaviors (Restrict/Cascade)
- Concurrency tokens on all entities with financial impact

## Authentication & Authorization

Uses ASP.NET Identity with cookie authentication. Authentication is **username-based** (not email-based), since residents may not have email addresses.

### Roles (exactly 3)

| Role | Access |
|------|--------|
| **Administrator** | Full access: users, apartments, houses, financial items, billing, reports |
| **Worker** | Monthly usage entry only |
| **Resident** | View own debt, bills, payment status only |

### Default Admin
- Username: `admin`
- Password: `Admin123`

## Billing Workflow

1. **Administrator enters Final Amount** for each active financial item
2. **System calculates** each house's bill based on calculation type
3. **Administrator reviews** generated draft bills (may edit amounts/descriptions)
4. **Administrator approves** — bills become immutable, house debt increases
5. **Administrator records payment** — bill marked paid, house debt decreases

### Financial Calculation Algorithms

#### Equal Division
```
House Amount = Final Amount / Number of Active Houses
```

#### Grouping
1. Houses sorted by usage count (from MonthlyUsage)
2. Houses divided into N groups (configured on financial item)
3. Each group assigned point values (FinancialItemGroupPoint)
4. Each house inherits its group's point value
5. Formula: `House Amount = (House Points / Sum of All Points) × Final Amount`
6. Rounding adjustment applied to last item to preserve total precision

### Period Types
- **Once**: Applied once, then auto-deactivated after approval
- **Permanent**: Applied every month until disabled
- **Installment**: `Monthly Amount = Total Amount / Number of Installments`, auto-deactivated when all installments complete

## Reports

Administrator dashboard provides:
- **Total billed** (approved + paid bills)
- **Total paid**
- **Outstanding debt**
- **Collection rate** (%)
- **Debt per apartment**
- **Debt per house**
- **Monthly breakdown** (billed vs paid per month)

Filterable by: Year, Month, House

## Testing

Test project: `ResidentialComplex.Tests` (25 integration tests)

Uses **SQLite in-memory** database with automatic schema creation.

### Test Coverage
- CRUD operations (Apartments, Houses, Financial Items)
- Equal division billing calculation
- Grouping billing calculation
- Installment lifecycle (deactivation after completion)
- Bill approval and debt increase
- Payment and debt decrease
- Bill uniqueness constraint
- Monthly usage unique constraint
- Negative debt (credit)
- Audit logging
- Identity (user creation and role assignment)
- Validation (payment on draft bill, no active houses)
- Once-type financial item deactivation

### Running Tests
```bash
dotnet test ResidentialComplex.slnx
```

## Configuration

### appsettings.json
```json
{
  "Database": {
    "Provider": "Sqlite",
    "ConnectionString": "Data Source=ResidentialComplex.db"
  }
}
```

### Switching Providers

**SQLite** (development/testing):
```json
{
  "Database": {
    "Provider": "Sqlite",
    "ConnectionString": "Data Source=ResidentialComplex.db"
  }
}
```

**SQL Server** (production):
```json
{
  "Database": {
    "Provider": "SqlServer",
    "ConnectionString": "Server=.;Database=ResidentialComplex;Trusted_Connection=true;TrustServerCertificate=true"
  }
}
```

## Running the Application

```bash
# Restore dependencies
dotnet restore ResidentialComplex.slnx

# Build
dotnet build ResidentialComplex.slnx

# Run
dotnet run --project src/ResidentialComplex.Web

# Run tests
dotnet test ResidentialComplex.slnx
```

The application automatically:
- Applies pending migrations on startup
- Seeds the three roles (Administrator, Worker, Resident)
- Seeds a default administrator user

## UI

- **Language**: Persian (Farsi)
- **Direction**: RTL
- **Framework**: Bootstrap 5 (RTL variant)
- **Rendering**: Blazor Server with Interactive Server render mode
- **Help System**: Contextual help drawer on every page (except Login) with page purpose, field explanations, business rules, workflow, and common mistakes

## Future Extension Points

- SMS/Email notifications for bill generation and payment reminders
- Online payment gateway integration
- Multi-complex support (multiple residential complexes)
- Document attachment for payments (receipt images)
- Export reports to PDF/Excel
- Mobile app with REST API
- Maintenance request tracking
- Parking management
- Common area booking
