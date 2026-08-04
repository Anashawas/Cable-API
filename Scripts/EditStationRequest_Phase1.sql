-- =============================================
-- Edit Station Request Phase 1: whitelist + diff snapshot
-- Cable EV Charging Station Management
-- =============================================
-- Per EDIT_STATION_REQUEST_BE_REQUIREMENTS.md (R1 + R2):
-- 1. ChargingPointUpdateRequest.StatusId       — owner may request open/closed change
-- 2. ChargingPointUpdateRequest.OldValuesJson  — snapshot of the station's values
--    for the changed fields, taken at SUBMIT time (diff baseline for review)
-- 3. DROP ChargerPointTypeId / StationTypeId / HasOffer — removed from the
--    owner-editable whitelist (admin-only via UpdateChargingPoint)
--
-- Run on db_ab1977_cableproduction BEFORE deploying the build that contains
-- the edit-station-request changes. Idempotent: safe to re-run.
-- (Both databases have no historical update-request rows, so the column drops
--  lose no data.)
-- =============================================

-- 1. StatusId
IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.ChargingPointUpdateRequest') AND name = 'StatusId')
BEGIN
    ALTER TABLE [dbo].[ChargingPointUpdateRequest] ADD [StatusId] INT NULL;
    PRINT 'Added column: ChargingPointUpdateRequest.StatusId';
END
GO

-- 2. OldValuesJson
IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.ChargingPointUpdateRequest') AND name = 'OldValuesJson')
BEGIN
    ALTER TABLE [dbo].[ChargingPointUpdateRequest] ADD [OldValuesJson] NVARCHAR(MAX) NULL;
    PRINT 'Added column: ChargingPointUpdateRequest.OldValuesJson';
END
GO

-- 3. Drop de-whitelisted columns
IF EXISTS (SELECT * FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.ChargingPointUpdateRequest') AND name = 'ChargerPointTypeId')
BEGIN
    ALTER TABLE [dbo].[ChargingPointUpdateRequest] DROP COLUMN [ChargerPointTypeId];
    PRINT 'Dropped column: ChargingPointUpdateRequest.ChargerPointTypeId';
END
GO

IF EXISTS (SELECT * FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.ChargingPointUpdateRequest') AND name = 'StationTypeId')
BEGIN
    ALTER TABLE [dbo].[ChargingPointUpdateRequest] DROP COLUMN [StationTypeId];
    PRINT 'Dropped column: ChargingPointUpdateRequest.StationTypeId';
END
GO

IF EXISTS (SELECT * FROM sys.columns
           WHERE object_id = OBJECT_ID('dbo.ChargingPointUpdateRequest') AND name = 'HasOffer')
BEGIN
    ALTER TABLE [dbo].[ChargingPointUpdateRequest] DROP COLUMN [HasOffer];
    PRINT 'Dropped column: ChargingPointUpdateRequest.HasOffer';
END
GO

-- 4. Notification types for the workflow (admins on submit, owner on decision).
--    Resolved by NAME at runtime, so ids may differ between environments.
INSERT INTO [dbo].[NotificationType] ([Name], [Description])
SELECT v.Name, v.Description
FROM (VALUES
    ('update_request_submitted', 'A provider submitted a station update request (sent to admins)'),
    ('update_request_decided',   'A station update request was approved or rejected (sent to the owner)')
) AS v(Name, Description)
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[NotificationType] t WHERE t.Name = v.Name);
GO
