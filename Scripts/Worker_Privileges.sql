-- Partner app: what a worker may see / do, chosen by the owner.
-- ProviderManager.Privileges = comma-separated keys (see Cable.Core WorkerPrivileges);
-- NULL = everything (existing workers keep their current access). Idempotent.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.ProviderManager', 'Privileges') IS NULL
BEGIN
    ALTER TABLE dbo.ProviderManager ADD Privileges NVARCHAR(500) NULL;
    PRINT 'Added ProviderManager.Privileges';
END
ELSE PRINT 'SKIP ProviderManager.Privileges exists';
GO
