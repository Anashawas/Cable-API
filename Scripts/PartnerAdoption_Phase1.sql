/* =============================================================================
   Cable — Partner app adoption (Sprint 5 follow-up)
   -----------------------------------------------------------------------------
   Adds two partner-scoped activity columns to dbo.UserAccount:
     PartnerLastLoginAt  — last sign-in via the provider app or partner web
     PartnerLastSeenAt   — last authenticated request from either of those

   LastLoginAt / LastSeenAt already exist but are written by every client,
   consumer app included, and most owners are also drivers — so they cannot
   say whether the owner uses the PARTNER app. Both new columns start NULL and
   fill as owners sign in / act; "has ever used the app" is meanwhile answered
   by the existing ProviderSecurityStamp / ProviderWebSecurityStamp columns.

   Idempotent. Additive, zero downtime. Run on dev first, then production.
   ============================================================================= */
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

IF COL_LENGTH('dbo.UserAccount', 'PartnerLastLoginAt') IS NULL
BEGIN
    ALTER TABLE dbo.UserAccount ADD PartnerLastLoginAt DATETIME NULL;
    PRINT 'Added UserAccount.PartnerLastLoginAt';
END ELSE PRINT 'UserAccount.PartnerLastLoginAt exists - skipped';

IF COL_LENGTH('dbo.UserAccount', 'PartnerLastSeenAt') IS NULL
BEGIN
    ALTER TABLE dbo.UserAccount ADD PartnerLastSeenAt DATETIME NULL;
    PRINT 'Added UserAccount.PartnerLastSeenAt';
END ELSE PRINT 'UserAccount.PartnerLastSeenAt exists - skipped';
GO
