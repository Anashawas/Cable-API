/* =============================================================================
   Cable — IsTest flag (demo/QA records hidden from public discovery)
   -----------------------------------------------------------------------------
   Adds dbo.ChargingPoint.IsTest and dbo.ServiceProvider.IsTest.

   Records with IsTest = 1 are excluded from the PUBLIC discovery queries
   (GetAllChargingPoints, GetChargingPointsPaged) but remain fully visible to
   their owner (GetMyChargingPoints) and when fetched directly by Id.

   Requires the matching backend build to be deployed — without it the filter
   is not applied and test rows WILL show publicly.

   Idempotent. Already applied on dev and production.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE Name = N'IsTest' AND Object_ID = Object_ID(N'dbo.ChargingPoint'))
BEGIN
    ALTER TABLE dbo.ChargingPoint
        ADD IsTest BIT NOT NULL CONSTRAINT DF_ChargingPoint_IsTest DEFAULT 0;
END;

IF NOT EXISTS (SELECT 1 FROM sys.columns
               WHERE Name = N'IsTest' AND Object_ID = Object_ID(N'dbo.ServiceProvider'))
BEGIN
    ALTER TABLE dbo.ServiceProvider
        ADD IsTest BIT NOT NULL CONSTRAINT DF_ServiceProvider_IsTest DEFAULT 0;
END;

SELECT
 (SELECT COUNT(*) FROM sys.columns WHERE Name=N'IsTest' AND Object_ID=Object_ID(N'dbo.ChargingPoint'))   AS CP_IsTest,
 (SELECT COUNT(*) FROM sys.columns WHERE Name=N'IsTest' AND Object_ID=Object_ID(N'dbo.ServiceProvider')) AS SP_IsTest;

/* -----------------------------------------------------------------------------
   App Store review demo account (created on PRODUCTION 2026-06-22)
     UserAccount.Id = 24071   Email = Test@gmail.com   Phone = 962790000000
     Role           = Provider (4)
     ChargingPoint.Id = 241   "Cable Demo Station (Test)"   IsTest = 1

   The account's OTP is bypassed via appsettings OtpSettings.TestPhoneNumbers.

   CLEANUP AFTER APPLE APPROVES — run all three:
     1) Remove "962790000000" from OtpSettings.TestPhoneNumbers and restart the app.
     2) UPDATE dbo.UserAccount   SET IsActive = 0, IsDeleted = 1 WHERE Id = 24071;
     3) UPDATE dbo.ChargingPoint SET IsDeleted = 1               WHERE Id = 241;
   ----------------------------------------------------------------------------- */
