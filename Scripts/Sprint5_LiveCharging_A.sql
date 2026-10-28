SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

/* ==============================================================================
   Sprint 5 — live charging session, part A (Docs/Sprint5-LiveCharging-A.md)
   - 4.2 / 11.c  proximity rule: the phone's position and distance on the session
   - 4.4         a station's own time-of-use tariff (TouTariff.ChargingPointId)
   - 4.7         automatic points when a driver's session ends (off by default)
   - 13.4        the driver's car on the session; CarModel.ReportsSoc learning
   - N-5         "your car has finished — could you unplug it?" once per session
   Requires OcppConnect_Phase3.sql (OcppTransaction.StartSource / Cost*), the charge-limit
   columns (OcppConnector.PendingLimit*) and PriceAlerts_Phase1.sql (TouTariff).
   Idempotent: every step checks first. Safe to re-run.
   ============================================================================== */

-- ---------------------------------------------------------------------------
-- 1. OcppTransaction — where the app start was, the car, the tariff used, automatic points, parked push
-- ---------------------------------------------------------------------------
IF COL_LENGTH('dbo.OcppTransaction', 'StartLat') IS NULL
BEGIN
    ALTER TABLE dbo.OcppTransaction ADD StartLat FLOAT NULL;                 -- app start: phone latitude
    PRINT 'Added OcppTransaction.StartLat';
END
ELSE PRINT 'SKIP OcppTransaction.StartLat exists';
GO
IF COL_LENGTH('dbo.OcppTransaction', 'StartLng') IS NULL
BEGIN
    ALTER TABLE dbo.OcppTransaction ADD StartLng FLOAT NULL;                 -- app start: phone longitude
    PRINT 'Added OcppTransaction.StartLng';
END
ELSE PRINT 'SKIP OcppTransaction.StartLng exists';
GO
IF COL_LENGTH('dbo.OcppTransaction', 'StartAccuracyMeters') IS NULL
BEGIN
    ALTER TABLE dbo.OcppTransaction ADD StartAccuracyMeters INT NULL;        -- the fix's accuracy radius (m)
    PRINT 'Added OcppTransaction.StartAccuracyMeters';
END
ELSE PRINT 'SKIP OcppTransaction.StartAccuracyMeters exists';
GO
IF COL_LENGTH('dbo.OcppTransaction', 'StartDistanceMeters') IS NULL
BEGIN
    ALTER TABLE dbo.OcppTransaction ADD StartDistanceMeters INT NULL;        -- phone → station (m); NULL = unknown
    PRINT 'Added OcppTransaction.StartDistanceMeters';
END
ELSE PRINT 'SKIP OcppTransaction.StartDistanceMeters exists';
GO
IF COL_LENGTH('dbo.OcppTransaction', 'CarId') IS NULL
BEGIN
    ALTER TABLE dbo.OcppTransaction ADD CarId INT NULL;                      -- UserCar.Id (no FK: a car may be deleted, the session stays)
    PRINT 'Added OcppTransaction.CarId';
END
ELSE PRINT 'SKIP OcppTransaction.CarId exists';
GO
IF COL_LENGTH('dbo.OcppTransaction', 'TariffChargingPointId') IS NULL
BEGIN
    ALTER TABLE dbo.OcppTransaction ADD TariffChargingPointId INT NULL;      -- station whose own tariff priced it; NULL = national tariff
    PRINT 'Added OcppTransaction.TariffChargingPointId';
END
ELSE PRINT 'SKIP OcppTransaction.TariffChargingPointId exists';
GO
IF COL_LENGTH('dbo.OcppTransaction', 'PointsAwarded') IS NULL
BEGIN
    ALTER TABLE dbo.OcppTransaction ADD PointsAwarded INT NULL;              -- automatic points; 0 = decided, none; NULL = not decided
    PRINT 'Added OcppTransaction.PointsAwarded';
END
ELSE PRINT 'SKIP OcppTransaction.PointsAwarded exists';
GO
IF COL_LENGTH('dbo.OcppTransaction', 'PartnerTransactionId') IS NULL
BEGIN
    ALTER TABLE dbo.OcppTransaction ADD PartnerTransactionId INT NULL;       -- the completed PartnerTransaction (code OCPP-{Id})
    PRINT 'Added OcppTransaction.PartnerTransactionId';
END
ELSE PRINT 'SKIP OcppTransaction.PartnerTransactionId exists';
GO
IF COL_LENGTH('dbo.OcppTransaction', 'PointsProcessedAt') IS NULL
BEGIN
    ALTER TABLE dbo.OcppTransaction ADD PointsProcessedAt DATETIME2(0) NULL; -- "already decided" marker (idempotency)
    PRINT 'Added OcppTransaction.PointsProcessedAt';
END
ELSE PRINT 'SKIP OcppTransaction.PointsProcessedAt exists';
GO
IF COL_LENGTH('dbo.OcppTransaction', 'PointsSkipReason') IS NULL
BEGIN
    ALTER TABLE dbo.OcppTransaction ADD PointsSkipReason NVARCHAR(40) NULL;  -- noAgreement | userBlocked | stationBlocked | zeroCost | …
    PRINT 'Added OcppTransaction.PointsSkipReason';
END
ELSE PRINT 'SKIP OcppTransaction.PointsSkipReason exists';
GO
IF COL_LENGTH('dbo.OcppTransaction', 'ParkedNotifiedAt') IS NULL
BEGIN
    ALTER TABLE dbo.OcppTransaction ADD ParkedNotifiedAt DATETIME2(0) NULL;  -- N-5 driver push sent (once per session)
    PRINT 'Added OcppTransaction.ParkedNotifiedAt';
END
ELSE PRINT 'SKIP OcppTransaction.ParkedNotifiedAt exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_OcppTransaction_PointsPending' AND object_id = OBJECT_ID('dbo.OcppTransaction'))
BEGIN
    CREATE INDEX IX_OcppTransaction_PointsPending ON dbo.OcppTransaction (StoppedAt)
        WHERE PointsProcessedAt IS NULL AND StartedByUserId IS NOT NULL;
    PRINT 'Created IX_OcppTransaction_PointsPending';
END
ELSE PRINT 'SKIP IX_OcppTransaction_PointsPending exists';
GO

-- ---------------------------------------------------------------------------
-- 2. OcppConnector — the app start's context parked on the plug until its session opens
-- ---------------------------------------------------------------------------
IF COL_LENGTH('dbo.OcppConnector', 'PendingStartUserId') IS NULL
BEGIN
    ALTER TABLE dbo.OcppConnector ADD
        PendingStartUserId          INT          NULL,   -- the driver whose App session takes it
        PendingStartSetAt           DATETIME2(0) NULL,   -- valid for OcppVirtualTag.RemoteStartAuthorizeWindow (10 min)
        PendingStartLat             FLOAT        NULL,
        PendingStartLng             FLOAT        NULL,
        PendingStartAccuracyMeters  INT          NULL,
        PendingStartDistanceMeters  INT          NULL,
        PendingCarId                INT          NULL;
    PRINT 'Added OcppConnector.PendingStart* / PendingCarId';
END
ELSE PRINT 'SKIP OcppConnector.PendingStart* exist';
GO

-- ---------------------------------------------------------------------------
-- 3. TouTariff — a station's own tariff (NULL = the national tariff, as before)
-- ---------------------------------------------------------------------------
IF COL_LENGTH('dbo.TouTariff', 'ChargingPointId') IS NULL
BEGIN
    ALTER TABLE dbo.TouTariff ADD ChargingPointId INT NULL;
    PRINT 'Added TouTariff.ChargingPointId';
END
ELSE PRINT 'SKIP TouTariff.ChargingPointId exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_TouTariff_ChargingPoint')
BEGIN
    ALTER TABLE dbo.TouTariff ADD CONSTRAINT FK_TouTariff_ChargingPoint
        FOREIGN KEY (ChargingPointId) REFERENCES dbo.ChargingPoint (Id);
    PRINT 'Created FK_TouTariff_ChargingPoint';
END
ELSE PRINT 'SKIP FK_TouTariff_ChargingPoint exists';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_TouTariff_ChargingPoint' AND object_id = OBJECT_ID('dbo.TouTariff'))
BEGIN
    CREATE INDEX IX_TouTariff_ChargingPoint ON dbo.TouTariff (ChargingPointId, IsActive) WHERE ChargingPointId IS NOT NULL;
    PRINT 'Created IX_TouTariff_ChargingPoint';
END
ELSE PRINT 'SKIP IX_TouTariff_ChargingPoint exists';
GO

-- ---------------------------------------------------------------------------
-- 4. CarModel.ReportsSoc — learned from sessions (NULL = not seen yet, 1 = reported a battery %)
-- ---------------------------------------------------------------------------
IF COL_LENGTH('dbo.CarModel', 'ReportsSoc') IS NULL
BEGIN
    ALTER TABLE dbo.CarModel ADD ReportsSoc BIT NULL;
    PRINT 'Added CarModel.ReportsSoc';
END
ELSE PRINT 'SKIP CarModel.ReportsSoc exists';
GO

-- ---------------------------------------------------------------------------
-- 5. Settings (defaults = the behaviour with no row; the rows make them visible and editable)
--    Change them with PUT /api/admin/ocpp/live-charging-settings. If you switch automatic points on
--    with SQL instead, set ModifiedAt = GETUTCDATE() in the same UPDATE: only sessions that end after
--    that moment earn points.
-- ---------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.AppSetting WHERE [Key] = 'Ocpp.AppStart.MaxDistanceMeters' AND IsDeleted = 0)
BEGIN
    INSERT INTO dbo.AppSetting ([Key], Value, CreatedAt, IsDeleted) VALUES ('Ocpp.AppStart.MaxDistanceMeters', '300', GETUTCDATE(), 0);
    PRINT 'Seeded Ocpp.AppStart.MaxDistanceMeters = 300';
END
ELSE PRINT 'SKIP Ocpp.AppStart.MaxDistanceMeters exists';

IF NOT EXISTS (SELECT 1 FROM dbo.AppSetting WHERE [Key] = 'Ocpp.AppStart.RequireLocation' AND IsDeleted = 0)
BEGIN
    INSERT INTO dbo.AppSetting ([Key], Value, CreatedAt, IsDeleted) VALUES ('Ocpp.AppStart.RequireLocation', 'true', GETUTCDATE(), 0);
    PRINT 'Seeded Ocpp.AppStart.RequireLocation = true';
END
ELSE PRINT 'SKIP Ocpp.AppStart.RequireLocation exists';

IF NOT EXISTS (SELECT 1 FROM dbo.AppSetting WHERE [Key] = 'Ocpp.AutoPoints.Enabled' AND IsDeleted = 0)
BEGIN
    INSERT INTO dbo.AppSetting ([Key], Value, CreatedAt, IsDeleted) VALUES ('Ocpp.AutoPoints.Enabled', 'false', GETUTCDATE(), 0);
    PRINT 'Seeded Ocpp.AutoPoints.Enabled = false';
END
ELSE PRINT 'SKIP Ocpp.AutoPoints.Enabled exists';
GO

-- ---------------------------------------------------------------------------
-- 6. Notification type for the N-5 inbox row ("your car has finished — could you unplug it?")
-- ---------------------------------------------------------------------------
IF NOT EXISTS (SELECT 1 FROM dbo.NotificationType WHERE [Name] = 'charging_session_parked')
BEGIN
    INSERT INTO dbo.NotificationType ([Name], [Description], [DeepLinksTo], [NameEn], [NameAr])
    VALUES ('charging_session_parked', 'Cable Connect: the driver''s car finished charging but is still plugged in',
            'charging-point', N'Unplug reminder', N'تذكير بفصل السيارة');
    PRINT 'Seeded NotificationType charging_session_parked';
END
ELSE PRINT 'SKIP NotificationType charging_session_parked exists';
GO

PRINT 'Sprint5_LiveCharging_A.sql done';
GO
