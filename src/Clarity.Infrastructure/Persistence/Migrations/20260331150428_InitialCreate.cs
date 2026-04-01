using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Clarity.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ComparisonJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Mode = table.Column<string>(type: "TEXT", nullable: false),
                    LeftSnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RightSnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    ObjectTypeFilterJson = table.Column<string>(type: "TEXT", nullable: true, defaultValue: "[]"),
                    WorkloadFilterJson = table.Column<string>(type: "TEXT", nullable: true, defaultValue: "[]"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComparisonJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConsultantAnnotations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsultantAnnotations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    TagsJson = table.Column<string>(type: "TEXT", nullable: true, defaultValue: "[]"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EnvironmentRelations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceEnvironmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetEnvironmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RelationType = table.Column<string>(type: "TEXT", nullable: false),
                    Direction = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnvironmentRelations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Environments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Type = table.Column<string>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TenantDomain = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Environments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExportJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Format = table.Column<string>(type: "TEXT", nullable: false),
                    ExportProfileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ComparisonJobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    OutputPath = table.Column<string>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    SnapshotIdsJson = table.Column<string>(type: "TEXT", nullable: true, defaultValue: "[]"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExportJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExportProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IncludeRawData = table.Column<bool>(type: "INTEGER", nullable: false),
                    IncludeMetadata = table.Column<bool>(type: "INTEGER", nullable: false),
                    IncludeSummarySheet = table.Column<bool>(type: "INTEGER", nullable: false),
                    IncludedObjectTypesJson = table.Column<string>(type: "TEXT", nullable: true, defaultValue: "[]"),
                    IncludedWorkloadsJson = table.Column<string>(type: "TEXT", nullable: true, defaultValue: "[]"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExportProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InventoryObjects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CollectorRunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ObjectType = table.Column<string>(type: "TEXT", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    RawDataJson = table.Column<string>(type: "TEXT", nullable: true),
                    PropertiesJson = table.Column<string>(type: "TEXT", nullable: true, defaultValue: "{}"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryObjects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    IsImmutable = table.Column<bool>(type: "INTEGER", nullable: false),
                    FinalizedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    WorkloadScopeJson = table.Column<string>(type: "TEXT", nullable: true, defaultValue: "[]"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Snapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ComparisonResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ComparisonJobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkloadArea = table.Column<string>(type: "TEXT", nullable: false),
                    TotalAdded = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalRemoved = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalModified = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalUnchanged = table.Column<int>(type: "INTEGER", nullable: false),
                    DeltaItemsJson = table.Column<string>(type: "TEXT", nullable: true, defaultValue: "[]"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComparisonResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComparisonResults_ComparisonJobs_ComparisonJobId",
                        column: x => x.ComparisonJobId,
                        principalTable: "ComparisonJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuthConfigurations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkloadArea = table.Column<string>(type: "TEXT", nullable: false),
                    AuthType = table.Column<string>(type: "TEXT", nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    TenantId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CertificateThumbprint = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SecretReference = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthConfigurations_Environments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "Environments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkloadSelections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkloadArea = table.Column<string>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConfigStatus = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkloadSelections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkloadSelections_Environments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "Environments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CollectorRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkloadArea = table.Column<string>(type: "TEXT", nullable: false),
                    CollectorType = table.Column<string>(type: "TEXT", nullable: false),
                    CollectorVersion = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ItemsCollected = table.Column<int>(type: "INTEGER", nullable: false),
                    CommandsExecutedJson = table.Column<string>(type: "TEXT", nullable: true, defaultValue: "[]"),
                    ErrorsJson = table.Column<string>(type: "TEXT", nullable: true, defaultValue: "[]"),
                    PermissionsUsedJson = table.Column<string>(type: "TEXT", nullable: true, defaultValue: "[]"),
                    WarningsJson = table.Column<string>(type: "TEXT", nullable: true, defaultValue: "[]"),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectorRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CollectorRuns_Snapshots_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "Snapshots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthConfigurations_EnvironmentId",
                table: "AuthConfigurations",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectorRuns_SnapshotId",
                table: "CollectorRuns",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_ComparisonResults_ComparisonJobId",
                table: "ComparisonResults",
                column: "ComparisonJobId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsultantAnnotations_EntityId_EntityType",
                table: "ConsultantAnnotations",
                columns: new[] { "EntityId", "EntityType" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryObjects_ExternalId_ObjectType",
                table: "InventoryObjects",
                columns: new[] { "ExternalId", "ObjectType" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryObjects_SnapshotId_ObjectType",
                table: "InventoryObjects",
                columns: new[] { "SnapshotId", "ObjectType" });

            migrationBuilder.CreateIndex(
                name: "IX_Snapshots_CustomerId",
                table: "Snapshots",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Snapshots_EnvironmentId",
                table: "Snapshots",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkloadSelections_EnvironmentId_WorkloadArea",
                table: "WorkloadSelections",
                columns: new[] { "EnvironmentId", "WorkloadArea" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthConfigurations");

            migrationBuilder.DropTable(
                name: "CollectorRuns");

            migrationBuilder.DropTable(
                name: "ComparisonResults");

            migrationBuilder.DropTable(
                name: "ConsultantAnnotations");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "EnvironmentRelations");

            migrationBuilder.DropTable(
                name: "ExportJobs");

            migrationBuilder.DropTable(
                name: "ExportProfiles");

            migrationBuilder.DropTable(
                name: "InventoryObjects");

            migrationBuilder.DropTable(
                name: "WorkloadSelections");

            migrationBuilder.DropTable(
                name: "Snapshots");

            migrationBuilder.DropTable(
                name: "ComparisonJobs");

            migrationBuilder.DropTable(
                name: "Environments");
        }
    }
}
