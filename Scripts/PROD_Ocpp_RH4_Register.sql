/* =============================================================================
   Cable Connect - register the real charger RH4 on PRODUCTION (station 210, الرشدان)
   Target: db_ab1977_cableproduction - run AFTER PROD_Deploy_2026-10-13_Master.sql,
   right before the AWS charger server (ocpp.cable-app.com) is switched to the
   production database. Idempotent.

   Why a script and not the admin "Register charger" form: RH4 connects WITHOUT a
   password (no Basic auth configured on the unit, same as on dev). The admin form
   issues a password, and the charger would then be refused until someone sets that
   password on the unit at the station. Copying the dev registration keeps it working.

   Values copied from dev on 2026-10-13 (OcppChargePoint 6 + the station's 2 cards).
   Plugs (connectors) are not inserted: the charger reports them itself on connect.
   NOT included: the OcppConnect subscription - record the real payment in the
   production admin (Subscriptions), which also switches live sharing on.
   ============================================================================= */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

DECLARE @db SYSNAME = DB_NAME();
IF @db NOT IN (N'db_ab1977_cableproduction', N'CableProdRehearsal')
BEGIN
    RAISERROR('Wrong database: %s. Nothing was changed.', 16, 1, @db) WITH NOWAIT;
    SET NOEXEC ON;
END
GO

DECLARE @Station INT = 210;
-- Station 210 = الرشدان, owner 15251 on production (checked 2026-10-13). Checked by id + owner, not by the
-- Arabic name, so the check does not depend on how the editor reads the file's encoding.
IF NOT EXISTS (SELECT 1 FROM dbo.ChargingPoint WHERE Id = @Station AND IsDeleted = 0 AND OwnerId = 15251)
BEGIN
    RAISERROR('Station 210 (owner 15251) not found on this database - stopped.', 16, 1) WITH NOWAIT;
    SET NOEXEC ON;
END
GO

BEGIN TRAN;

IF NOT EXISTS (SELECT 1 FROM dbo.OcppChargePoint WHERE ChargePointId = N'RH4' AND IsDeleted = 0)
BEGIN
    INSERT INTO dbo.OcppChargePoint (ChargingPointId, ChargePointId, PasswordHash, DisplayName, HeartbeatInterval, IsEnabled, CreatedAt)
    VALUES (210, N'RH4', NULL, N'RH4', 60, 1, GETUTCDATE());
    PRINT 'Registered charger RH4 on station 210 (no password, like the unit is configured)';
END
ELSE PRINT 'RH4 already registered - skipped';

-- Cards accepted at this station (Accepted list). IdTags are stored trimmed + upper-case.
IF NOT EXISTS (SELECT 1 FROM dbo.OcppAuthorizedTag WHERE ChargingPointId = 210 AND IdTag = N'12345678' AND IsDeleted = 0)
    INSERT INTO dbo.OcppAuthorizedTag (ChargingPointId, IdTag, Label, IsEnabled, ExpiresAt, CreatedAt)
    VALUES (210, N'12345678', N'Station card (visit 8 Oct)', 1, NULL, GETUTCDATE());
IF NOT EXISTS (SELECT 1 FROM dbo.OcppAuthorizedTag WHERE ChargingPointId = 210 AND IdTag = N'26893AC1' AND IsDeleted = 0)
    INSERT INTO dbo.OcppAuthorizedTag (ChargingPointId, IdTag, Label, IsEnabled, ExpiresAt, CreatedAt)
    VALUES (210, N'26893AC1', NULL, 1, '2026-12-12T17:25:00', GETUTCDATE());

COMMIT;
GO

SELECT o.Id, o.ChargePointId, o.DisplayName, o.ChargingPointId, o.IsEnabled,
       CASE WHEN o.PasswordHash IS NULL THEN 'no password' ELSE 'password set' END AS Auth,
       (SELECT COUNT(*) FROM dbo.OcppAuthorizedTag t WHERE t.ChargingPointId = o.ChargingPointId AND t.IsDeleted = 0) AS Cards
FROM dbo.OcppChargePoint o WHERE o.ChargePointId = N'RH4' AND o.IsDeleted = 0;
GO
SET NOEXEC OFF;
GO
