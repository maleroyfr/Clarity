using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clarity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261126193706_IndexedInventorySearch : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Domain",
            table: "InventoryObjects",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "Enabled",
            table: "InventoryObjects",
            type: "bit",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Site",
            table: "InventoryObjects",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SearchName",
            table: "InventoryObjects",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true,
            computedColumnSql: "UPPER([DisplayName])",
            stored: true);

        migrationBuilder.AddColumn<string>(
            name: "SearchId",
            table: "ExternalKeys",
            type: "nvarchar(500)",
            maxLength: 500,
            nullable: true,
            computedColumnSql: "UPPER([ExternalId])",
            stored: true);

        migrationBuilder.CreateIndex(
            name: "IX_InventoryObjects_WorkspaceId_EnvironmentId_Domain",
            table: "InventoryObjects",
            columns: new[] { "WorkspaceId", "EnvironmentId", "Domain" });

        migrationBuilder.CreateIndex(
            name: "IX_InventoryObjects_WorkspaceId_EnvironmentId_Site",
            table: "InventoryObjects",
            columns: new[] { "WorkspaceId", "EnvironmentId", "Site" });

        migrationBuilder.CreateIndex(
            name: "IX_InventoryObjects_WorkspaceId_SearchName",
            table: "InventoryObjects",
            columns: new[] { "WorkspaceId", "SearchName" });

        migrationBuilder.CreateIndex(
            name: "IX_ExternalKeys_WorkspaceId_SearchId",
            table: "ExternalKeys",
            columns: new[] { "WorkspaceId", "SearchId" });

        migrationBuilder.CreateIndex(
            name: "IX_AlternateKeyPeriods_WorkspaceId_NormalizedValue",
            table: "AlternateKeyPeriods",
            columns: new[] { "WorkspaceId", "NormalizedValue" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_InventoryObjects_WorkspaceId_EnvironmentId_Domain",
            table: "InventoryObjects");

        migrationBuilder.DropIndex(
            name: "IX_InventoryObjects_WorkspaceId_EnvironmentId_Site",
            table: "InventoryObjects");

        migrationBuilder.DropIndex(
            name: "IX_InventoryObjects_WorkspaceId_SearchName",
            table: "InventoryObjects");

        migrationBuilder.DropIndex(
            name: "IX_ExternalKeys_WorkspaceId_SearchId",
            table: "ExternalKeys");

        migrationBuilder.DropIndex(
            name: "IX_AlternateKeyPeriods_WorkspaceId_NormalizedValue",
            table: "AlternateKeyPeriods");

        migrationBuilder.DropColumn(
            name: "SearchName",
            table: "InventoryObjects");

        migrationBuilder.DropColumn(
            name: "SearchId",
            table: "ExternalKeys");

        migrationBuilder.DropColumn(
            name: "Domain",
            table: "InventoryObjects");

        migrationBuilder.DropColumn(
            name: "Enabled",
            table: "InventoryObjects");

        migrationBuilder.DropColumn(
            name: "Site",
            table: "InventoryObjects");
    }
}
