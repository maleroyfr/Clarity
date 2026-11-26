using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clarity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261126195301_InventoryChangeDates : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LastChangedAtUtc",
            table: "InventoryObjects",
            type: "datetimeoffset",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_InventoryObjects_WorkspaceId_EnvironmentId_LastChangedAtUtc",
            table: "InventoryObjects",
            columns: new[] { "WorkspaceId", "EnvironmentId", "LastChangedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_InventoryObjects_WorkspaceId_EnvironmentId_LastChangedAtUtc",
            table: "InventoryObjects");

        migrationBuilder.DropColumn(
            name: "LastChangedAtUtc",
            table: "InventoryObjects");
    }
}
