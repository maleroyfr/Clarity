-- Run as the SQL Entra administrator in the Clarity database.
-- Replace names privately. The administrator needs directory lookup permissions.
CREATE USER [clarity-migrations] FROM EXTERNAL PROVIDER;
ALTER ROLE db_owner ADD MEMBER [clarity-migrations];

-- Run the reviewed EF bundle as clarity-migrations before the statements below.
CREATE USER [clarity-app] FROM EXTERNAL PROVIDER;
GRANT SELECT, INSERT, UPDATE ON dbo.ClientWorkspaces TO [clarity-app];
GRANT SELECT, INSERT, UPDATE ON dbo.Members TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Projects TO [clarity-app];
GRANT SELECT, INSERT, UPDATE, DELETE ON dbo.Environments TO [clarity-app];
-- DELETE serves audit retention only: the Security.AuditRetention block predicate refuses events younger than 29 days.
GRANT SELECT, INSERT, DELETE ON dbo.AuditEvents TO [clarity-app];
GRANT SELECT ON dbo.PlatformAdmins TO [clarity-app];
DENY ALTER ANY SECURITY POLICY TO [clarity-app];
DENY ALTER ANY ROLE TO [clarity-app];
DENY ALTER ON SCHEMA::dbo TO [clarity-app];
DENY ALTER ON SCHEMA::Security TO [clarity-app];

-- Replace placeholders with the first administrator's Entra tid and oid.
-- This grants platform administration, not client inventory access.
INSERT INTO dbo.PlatformAdmins (TenantId, ObjectId)
VALUES ('00000000-0000-0000-0000-000000000000', '00000000-0000-0000-0000-000000000000');
