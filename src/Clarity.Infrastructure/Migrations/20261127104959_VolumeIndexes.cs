using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clarity.Infrastructure.Migrations;

/// <inheritdoc />
public partial class _20261127104959_VolumeIndexes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_ExternalKeys_WorkspaceId_ObjectId_IsPrimary",
            table: "ExternalKeys",
            columns: new[] { "WorkspaceId", "ObjectId", "IsPrimary" })
            .Annotation("SqlServer:Include", new[] { "Namespace", "ExternalId" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_ExternalKeys_WorkspaceId_ObjectId_IsPrimary",
            table: "ExternalKeys");
    }
}
