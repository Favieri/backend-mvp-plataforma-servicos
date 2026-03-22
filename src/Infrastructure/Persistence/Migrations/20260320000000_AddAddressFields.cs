using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds address fields to User (default address) and Order (service address snapshot).
/// Also adds address snapshot fields to RecurringPlan.
///
/// IDEMPOTENT: uses IF NOT EXISTS guards so it can be safely re-applied when the schema
/// was already created outside EF Core.
/// </summary>
public partial class AddAddressFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ─── User: default address columns ────────────────────────────────────
        migrationBuilder.Sql(@"ALTER TABLE ""User"" ADD COLUMN IF NOT EXISTS ""addr_zip_code"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""User"" ADD COLUMN IF NOT EXISTS ""addr_street"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""User"" ADD COLUMN IF NOT EXISTS ""addr_number"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""User"" ADD COLUMN IF NOT EXISTS ""addr_neighborhood"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""User"" ADD COLUMN IF NOT EXISTS ""addr_city"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""User"" ADD COLUMN IF NOT EXISTS ""addr_state"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""User"" ADD COLUMN IF NOT EXISTS ""addr_complement"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""User"" ADD COLUMN IF NOT EXISTS ""addr_reference"" text;");

        // ─── Order: service address snapshot columns ───────────────────────────
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""svcAddrZipCode"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""svcAddrStreet"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""svcAddrNumber"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""svcAddrNeighborhood"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""svcAddrCity"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""svcAddrState"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""svcAddrComplement"" text;");
        migrationBuilder.Sql(@"ALTER TABLE ""Order"" ADD COLUMN IF NOT EXISTS ""svcAddrReference"" text;");

        // ─── RecurringPlan: service address snapshot columns ──────────────────
        migrationBuilder.Sql(@"ALTER TABLE recurring_plan ADD COLUMN IF NOT EXISTS ""svcAddrZipCode"" text;");
        migrationBuilder.Sql(@"ALTER TABLE recurring_plan ADD COLUMN IF NOT EXISTS ""svcAddrStreet"" text;");
        migrationBuilder.Sql(@"ALTER TABLE recurring_plan ADD COLUMN IF NOT EXISTS ""svcAddrNumber"" text;");
        migrationBuilder.Sql(@"ALTER TABLE recurring_plan ADD COLUMN IF NOT EXISTS ""svcAddrNeighborhood"" text;");
        migrationBuilder.Sql(@"ALTER TABLE recurring_plan ADD COLUMN IF NOT EXISTS ""svcAddrCity"" text;");
        migrationBuilder.Sql(@"ALTER TABLE recurring_plan ADD COLUMN IF NOT EXISTS ""svcAddrState"" text;");
        migrationBuilder.Sql(@"ALTER TABLE recurring_plan ADD COLUMN IF NOT EXISTS ""svcAddrComplement"" text;");
        migrationBuilder.Sql(@"ALTER TABLE recurring_plan ADD COLUMN IF NOT EXISTS ""svcAddrReference"" text;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // User
        migrationBuilder.DropColumn(name: "addr_zip_code",    table: "User");
        migrationBuilder.DropColumn(name: "addr_street",      table: "User");
        migrationBuilder.DropColumn(name: "addr_number",      table: "User");
        migrationBuilder.DropColumn(name: "addr_neighborhood",table: "User");
        migrationBuilder.DropColumn(name: "addr_city",        table: "User");
        migrationBuilder.DropColumn(name: "addr_state",       table: "User");
        migrationBuilder.DropColumn(name: "addr_complement",  table: "User");
        migrationBuilder.DropColumn(name: "addr_reference",   table: "User");

        // Order
        migrationBuilder.DropColumn(name: "svcAddrZipCode",       table: "Order");
        migrationBuilder.DropColumn(name: "svcAddrStreet",        table: "Order");
        migrationBuilder.DropColumn(name: "svcAddrNumber",        table: "Order");
        migrationBuilder.DropColumn(name: "svcAddrNeighborhood",  table: "Order");
        migrationBuilder.DropColumn(name: "svcAddrCity",          table: "Order");
        migrationBuilder.DropColumn(name: "svcAddrState",         table: "Order");
        migrationBuilder.DropColumn(name: "svcAddrComplement",    table: "Order");
        migrationBuilder.DropColumn(name: "svcAddrReference",     table: "Order");

        // RecurringPlan
        migrationBuilder.DropColumn(name: "svcAddrZipCode",       table: "recurring_plan");
        migrationBuilder.DropColumn(name: "svcAddrStreet",        table: "recurring_plan");
        migrationBuilder.DropColumn(name: "svcAddrNumber",        table: "recurring_plan");
        migrationBuilder.DropColumn(name: "svcAddrNeighborhood",  table: "recurring_plan");
        migrationBuilder.DropColumn(name: "svcAddrCity",          table: "recurring_plan");
        migrationBuilder.DropColumn(name: "svcAddrState",         table: "recurring_plan");
        migrationBuilder.DropColumn(name: "svcAddrComplement",    table: "recurring_plan");
        migrationBuilder.DropColumn(name: "svcAddrReference",     table: "recurring_plan");
    }
}
