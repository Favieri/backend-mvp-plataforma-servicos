using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations;

/// <summary>
/// Baseline migration for adding EF Core to an existing PostgreSQL database.
///
/// IMPORTANT FOR EXISTING DATABASES:
/// The tables already exist in production. Apply this migration as follows:
///   1. Run the idempotent SQL script: artifacts/sql/001_initial_idempotent.sql
///   2. OR mark the migration as applied without running Up():
///      INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
///      VALUES ('20260306000000_InitialEfBaseline', '8.0.10');
///
/// The Up() method only adds NEW indexes not present in the original schema.
/// All existing tables and constraints are left untouched.
/// </summary>
public partial class InitialEfBaseline : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // New indexes on User (existing table — only new ones added)
        migrationBuilder.CreateIndex(
            name: "IX_User_zoneId",
            table: "User",
            column: "zoneId");

        // New indexes on Professional
        migrationBuilder.CreateIndex(
            name: "IX_Professional_userId",
            table: "Professional",
            column: "userId");

        migrationBuilder.CreateIndex(
            name: "IX_Professional_active_rating",
            table: "Professional",
            columns: ["active", "rating"]);

        // New indexes on Order
        migrationBuilder.CreateIndex(
            name: "IX_Order_clientId",
            table: "Order",
            column: "clientId");

        migrationBuilder.CreateIndex(
            name: "IX_Order_serviceId",
            table: "Order",
            column: "serviceId");

        migrationBuilder.CreateIndex(
            name: "IX_Order_clientId_createdAt",
            table: "Order",
            columns: ["clientId", "createdAt"]);

        migrationBuilder.CreateIndex(
            name: "IX_Order_status",
            table: "Order",
            column: "status");

        // New indexes on Appointment
        migrationBuilder.CreateIndex(
            name: "IX_Appointment_professionalId",
            table: "Appointment",
            column: "professionalId");

        migrationBuilder.CreateIndex(
            name: "IX_Appointment_clientId",
            table: "Appointment",
            column: "clientId");

        migrationBuilder.CreateIndex(
            name: "IX_Appointment_professionalId_startsAt_status",
            table: "Appointment",
            columns: ["professionalId", "startsAt", "status"]);

        // New indexes on Conversation
        migrationBuilder.CreateIndex(
            name: "IX_Conversation_clientId",
            table: "Conversation",
            column: "clientId");

        migrationBuilder.CreateIndex(
            name: "IX_Conversation_professionalId",
            table: "Conversation",
            column: "professionalId");

        migrationBuilder.CreateIndex(
            name: "IX_Conversation_clientId_professionalId",
            table: "Conversation",
            columns: ["clientId", "professionalId"]);

        // New indexes on Message
        migrationBuilder.CreateIndex(
            name: "IX_Message_conversationId",
            table: "Message",
            column: "conversationId");

        migrationBuilder.CreateIndex(
            name: "IX_Message_conversationId_sentAt",
            table: "Message",
            columns: ["conversationId", "sentAt"]);

        // New indexes on Review
        migrationBuilder.CreateIndex(
            name: "IX_Review_professionalId",
            table: "Review",
            column: "professionalId");

        migrationBuilder.CreateIndex(
            name: "IX_Review_clientId_createdAt",
            table: "Review",
            columns: ["clientId", "createdAt"]);

        // New indexes on ProfessionalService
        migrationBuilder.CreateIndex(
            name: "IX_ProfessionalService_professionalId",
            table: "ProfessionalService",
            column: "professionalId");

        migrationBuilder.CreateIndex(
            name: "IX_ProfessionalService_serviceId",
            table: "ProfessionalService",
            column: "serviceId");

        // New indexes on ProfessionalZone
        migrationBuilder.CreateIndex(
            name: "IX_ProfessionalZone_zoneId",
            table: "ProfessionalZone",
            column: "zoneId");

        // New indexes on ProfessionalAvailability
        migrationBuilder.CreateIndex(
            name: "IX_ProfessionalAvailability_professionalId_weekday",
            table: "ProfessionalAvailability",
            columns: ["professionalId", "weekday"]);

        // New indexes on ProfessionalBlock
        migrationBuilder.CreateIndex(
            name: "IX_ProfessionalBlock_professionalId_startsAt_endsAt",
            table: "ProfessionalBlock",
            columns: ["professionalId", "startsAt", "endsAt"]);

        // New indexes on ProfessionalPortfolio
        migrationBuilder.CreateIndex(
            name: "IX_ProfessionalPortfolio_professionalId",
            table: "ProfessionalPortfolio",
            column: "professionalId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_User_zoneId", "User");
        migrationBuilder.DropIndex("IX_Professional_userId", "Professional");
        migrationBuilder.DropIndex("IX_Professional_active_rating", "Professional");
        migrationBuilder.DropIndex("IX_Order_clientId", "Order");
        migrationBuilder.DropIndex("IX_Order_serviceId", "Order");
        migrationBuilder.DropIndex("IX_Order_clientId_createdAt", "Order");
        migrationBuilder.DropIndex("IX_Order_status", "Order");
        migrationBuilder.DropIndex("IX_Appointment_professionalId", "Appointment");
        migrationBuilder.DropIndex("IX_Appointment_clientId", "Appointment");
        migrationBuilder.DropIndex("IX_Appointment_professionalId_startsAt_status", "Appointment");
        migrationBuilder.DropIndex("IX_Conversation_clientId", "Conversation");
        migrationBuilder.DropIndex("IX_Conversation_professionalId", "Conversation");
        migrationBuilder.DropIndex("IX_Conversation_clientId_professionalId", "Conversation");
        migrationBuilder.DropIndex("IX_Message_conversationId", "Message");
        migrationBuilder.DropIndex("IX_Message_conversationId_sentAt", "Message");
        migrationBuilder.DropIndex("IX_Review_professionalId", "Review");
        migrationBuilder.DropIndex("IX_Review_clientId_createdAt", "Review");
        migrationBuilder.DropIndex("IX_ProfessionalService_professionalId", "ProfessionalService");
        migrationBuilder.DropIndex("IX_ProfessionalService_serviceId", "ProfessionalService");
        migrationBuilder.DropIndex("IX_ProfessionalZone_zoneId", "ProfessionalZone");
        migrationBuilder.DropIndex("IX_ProfessionalAvailability_professionalId_weekday", "ProfessionalAvailability");
        migrationBuilder.DropIndex("IX_ProfessionalBlock_professionalId_startsAt_endsAt", "ProfessionalBlock");
        migrationBuilder.DropIndex("IX_ProfessionalPortfolio_professionalId", "ProfessionalPortfolio");
    }
}
