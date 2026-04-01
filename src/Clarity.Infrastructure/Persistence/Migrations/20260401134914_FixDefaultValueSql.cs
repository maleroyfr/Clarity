using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clarity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixDefaultValueSql : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "WorkloadScopeJson",
                table: "Snapshots",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "'[]'",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true,
                oldDefaultValue: "[]");

            migrationBuilder.AlterColumn<string>(
                name: "PropertiesJson",
                table: "InventoryObjects",
                type: "TEXT",
                nullable: false,
                defaultValueSql: "'{}'",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true,
                oldDefaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "WorkloadScopeJson",
                table: "Snapshots",
                type: "TEXT",
                nullable: true,
                defaultValue: "[]",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldDefaultValueSql: "'[]'");

            migrationBuilder.AlterColumn<string>(
                name: "PropertiesJson",
                table: "InventoryObjects",
                type: "TEXT",
                nullable: true,
                defaultValue: "{}",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldDefaultValueSql: "'{}'");
        }
    }
}
