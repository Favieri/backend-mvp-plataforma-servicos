using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds address fields to User (default address) and Order (service address snapshot).
/// Also adds address snapshot fields to RecurringPlan.
/// </summary>
public partial class AddAddressFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ─── User: default address columns ────────────────────────────────────
        migrationBuilder.AddColumn<string>(
            name: "addr_zip_code",
            table: "User",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "addr_street",
            table: "User",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "addr_number",
            table: "User",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "addr_neighborhood",
            table: "User",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "addr_city",
            table: "User",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "addr_state",
            table: "User",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "addr_complement",
            table: "User",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "addr_reference",
            table: "User",
            type: "text",
            nullable: true);

        // ─── Order: service address snapshot columns ──────────────────────────
        migrationBuilder.AddColumn<string>(
            name: "svcAddrZipCode",
            table: "Order",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "svcAddrStreet",
            table: "Order",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "svcAddrNumber",
            table: "Order",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "svcAddrNeighborhood",
            table: "Order",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "svcAddrCity",
            table: "Order",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "svcAddrState",
            table: "Order",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "svcAddrComplement",
            table: "Order",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "svcAddrReference",
            table: "Order",
            type: "text",
            nullable: true);

        // ─── RecurringPlan: service address snapshot columns ──────────────────
        migrationBuilder.AddColumn<string>(
            name: "svcAddrZipCode",
            table: "recurring_plan",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "svcAddrStreet",
            table: "recurring_plan",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "svcAddrNumber",
            table: "recurring_plan",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "svcAddrNeighborhood",
            table: "recurring_plan",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "svcAddrCity",
            table: "recurring_plan",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "svcAddrState",
            table: "recurring_plan",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "svcAddrComplement",
            table: "recurring_plan",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "svcAddrReference",
            table: "recurring_plan",
            type: "text",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // User
        migrationBuilder.DropColumn(name: "addr_zip_code", table: "User");
        migrationBuilder.DropColumn(name: "addr_street", table: "User");
        migrationBuilder.DropColumn(name: "addr_number", table: "User");
        migrationBuilder.DropColumn(name: "addr_neighborhood", table: "User");
        migrationBuilder.DropColumn(name: "addr_city", table: "User");
        migrationBuilder.DropColumn(name: "addr_state", table: "User");
        migrationBuilder.DropColumn(name: "addr_complement", table: "User");
        migrationBuilder.DropColumn(name: "addr_reference", table: "User");

        // Order
        migrationBuilder.DropColumn(name: "svcAddrZipCode", table: "Order");
        migrationBuilder.DropColumn(name: "svcAddrStreet", table: "Order");
        migrationBuilder.DropColumn(name: "svcAddrNumber", table: "Order");
        migrationBuilder.DropColumn(name: "svcAddrNeighborhood", table: "Order");
        migrationBuilder.DropColumn(name: "svcAddrCity", table: "Order");
        migrationBuilder.DropColumn(name: "svcAddrState", table: "Order");
        migrationBuilder.DropColumn(name: "svcAddrComplement", table: "Order");
        migrationBuilder.DropColumn(name: "svcAddrReference", table: "Order");

        // RecurringPlan
        migrationBuilder.DropColumn(name: "svcAddrZipCode", table: "RecurringPlan");
        migrationBuilder.DropColumn(name: "svcAddrStreet", table: "RecurringPlan");
        migrationBuilder.DropColumn(name: "svcAddrNumber", table: "RecurringPlan");
        migrationBuilder.DropColumn(name: "svcAddrNeighborhood", table: "RecurringPlan");
        migrationBuilder.DropColumn(name: "svcAddrCity", table: "RecurringPlan");
        migrationBuilder.DropColumn(name: "svcAddrState", table: "RecurringPlan");
        migrationBuilder.DropColumn(name: "svcAddrComplement", table: "RecurringPlan");
        migrationBuilder.DropColumn(name: "svcAddrReference", table: "RecurringPlan");
    }
}
