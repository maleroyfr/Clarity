-- Run as the SQL Entra administrator in the Clarity database.
-- Replace names privately. The administrator needs directory lookup permissions.
CREATE USER [clarity-migrations] FROM EXTERNAL PROVIDER;
ALTER ROLE db_owner ADD MEMBER [clarity-migrations];

-- Run the reviewed EF bundle as clarity-migrations before the statements below.
-- Application statements, from CREATE USER to the last DENY: one statement per line, no blank line in between.
-- SQL tests apply this block to a disposable login (tests/Shared/ApplicationLogin.cs) and check that it grants
-- every table of the EF model; a migration that adds a table adds its GRANT here in the same release.
CREATE USER [clarity-app] FROM EXTERNAL PROVIDER;
-- Workspace-level tables: no DELETE. Removing a membership or a platform administrator is a SQL administrator change.
GRANT SELECT, INSERT, UPDATE ON dbo.ClientWorkspaces TO [clarity-app];
GRANT SELECT, INSERT, UPDATE ON dbo.Members TO [clarity-app];
GRANT SELECT, INSERT, UPDATE ON dbo.Connections TO [clarity-app];
GRANT SELECT ON dbo.PlatformAdmins TO [clarity-app];
-- DELETE serves audit retention only: the Security.AuditRetention block predicate refuses events younger than 29 days.
GRANT SELECT, INSERT, DELETE ON dbo.AuditEvents TO [clarity-app];
-- Project-owned tables: DELETE serves recomputation and controlled project deletion (docs/retention-deletion.md).
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.AlternateKeyPeriods TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Assessments TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.CollectionJobs TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.CollectionRequests TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.CollectionRuns TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.CurrentValues TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.DependencyAnalyses TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.DependencyLinks TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.DependencyResources TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Environments TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ExportArtifacts TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ExportJobs TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ExternalKeys TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.GraphCursors TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.InventoryObjects TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.InventoryRelationships TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.MappingDecisions TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.MappingRevisions TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ObjectObservations TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.PopulationEntries TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.PopulationMembers TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Populations TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Projects TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.RawArtifacts TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.RelationshipObservations TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.RelationshipSupports TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ReviewFlags TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.RunComparisonItems TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.RunComparisons TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ScopeDecisionRevisions TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.ScopeDecisions TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SectionRuns TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.SyncBindings TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.TargetComparisonItems TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.TargetComparisons TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.TargetMappingRules TO [clarity-app];
DENY ALTER ANY SECURITY POLICY TO [clarity-app];
DENY ALTER ANY ROLE TO [clarity-app];
DENY ALTER ON SCHEMA::dbo TO [clarity-app];
DENY ALTER ON SCHEMA::Security TO [clarity-app];

-- Replace placeholders with the first administrator's Entra tid and oid.
-- This grants platform administration, not client inventory access.
INSERT INTO dbo.PlatformAdmins (TenantId, ObjectId)
VALUES ('00000000-0000-0000-0000-000000000000', '00000000-0000-0000-0000-000000000000');
