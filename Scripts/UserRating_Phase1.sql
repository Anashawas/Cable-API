/* =============================================================================
   Cable — Driver rating Phase 1
   -----------------------------------------------------------------------------
   - Creates dbo.UserRate: the provider's rating OF a driver, the reverse
     direction of dbo.Rate (driver rates station).

   Every row is anchored to one COMPLETED dbo.PartnerTransaction — the only
   server-side record that a given driver was actually served at a given
   provider. A filtered unique index on PartnerTransactionId enforces one
   rating per visit, which is what keeps providers from rating a customer
   repeatedly or rating someone they never served.

   No AVGRating column by design: the average is computed on read. The
   denormalized average on dbo.Rate / dbo.ServiceProviderRate goes stale as
   soon as a row is edited or soft-deleted, and there is no volume here to
   justify carrying that problem forward.

   QUOTED_IDENTIFIER must be ON to create the filtered indexes below. sqlcmd
   connects with it OFF, so setting it here is required, not decorative —
   without it the table is created and every index silently fails (Msg 1934).

   Idempotent PER OBJECT, not just per table: the table and each index are
   checked separately, so a partially-applied run (table created, indexes
   failed) is repaired by simply re-running this script.

   Run on dev first, then production (additive, zero downtime).
   ============================================================================= */

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;
GO

-- 1) Table -------------------------------------------------------------------
IF OBJECT_ID(N'dbo.UserRate', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.UserRate (
        Id                   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserRate PRIMARY KEY,
        UserId               INT           NOT NULL,   -- the driver being rated
        RatedByUserId        INT           NOT NULL,   -- owner / worker / admin who rated
        ProviderType         NVARCHAR(50)  NOT NULL,   -- "ChargingPoint" | "ServiceProvider"
        ProviderId           INT           NOT NULL,
        PartnerTransactionId INT           NOT NULL,   -- the anchor
        Rating               INT           NOT NULL,   -- 1..5
        Comment              NVARCHAR(1000) NULL,
        IsDeleted            BIT           NOT NULL CONSTRAINT DF_UserRate_IsDeleted DEFAULT 0,
        CreatedBy            INT           NULL,
        CreatedAt            DATETIME      NOT NULL CONSTRAINT DF_UserRate_CreatedAt DEFAULT GETDATE(),
        ModifiedBy           INT           NULL,
        ModifiedAt           DATETIME      NULL,

        -- NO ACTION on both UserAccount FKs: two cascade paths into the same
        -- table is rejected by SQL Server.
        CONSTRAINT FK_UserRate_UserAccount
            FOREIGN KEY (UserId) REFERENCES dbo.UserAccount(Id),
        CONSTRAINT FK_UserRate_RatedByUserAccount
            FOREIGN KEY (RatedByUserId) REFERENCES dbo.UserAccount(Id),
        CONSTRAINT FK_UserRate_PartnerTransaction
            FOREIGN KEY (PartnerTransactionId) REFERENCES dbo.PartnerTransaction(Id),

        CONSTRAINT CK_UserRate_ProviderType
            CHECK (ProviderType IN (N'ServiceProvider', N'ChargingPoint')),
        CONSTRAINT CK_UserRate_Rating
            CHECK (Rating BETWEEN 1 AND 5),
        -- A provider rating itself would be self-dealing on its own average.
        CONSTRAINT CK_UserRate_NoSelfRating
            CHECK (UserId <> RatedByUserId)
    );

    PRINT 'Created dbo.UserRate.';
END
ELSE
    PRINT 'dbo.UserRate already exists - skipped.';
GO

-- 2) Indexes (each independently idempotent) ---------------------------------

-- One rating per visit. Filtered so a soft-deleted rating frees the slot.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.UserRate')
                 AND name = N'UX_UserRate_PartnerTransactionId')
BEGIN
    CREATE UNIQUE INDEX UX_UserRate_PartnerTransactionId
        ON dbo.UserRate(PartnerTransactionId)
        WHERE IsDeleted = 0;
    PRINT 'Created UX_UserRate_PartnerTransactionId.';
END
ELSE
    PRINT 'UX_UserRate_PartnerTransactionId already exists - skipped.';
GO

-- Drives the driver's average and the provider-facing lookup.
IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.UserRate')
                 AND name = N'IX_UserRate_UserId')
BEGIN
    CREATE INDEX IX_UserRate_UserId
        ON dbo.UserRate(UserId)
        INCLUDE (Rating)
        WHERE IsDeleted = 0;
    PRINT 'Created IX_UserRate_UserId.';
END
ELSE
    PRINT 'IX_UserRate_UserId already exists - skipped.';
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes
               WHERE object_id = OBJECT_ID(N'dbo.UserRate')
                 AND name = N'IX_UserRate_Provider')
BEGIN
    CREATE INDEX IX_UserRate_Provider
        ON dbo.UserRate(ProviderType, ProviderId)
        WHERE IsDeleted = 0;
    PRINT 'Created IX_UserRate_Provider.';
END
ELSE
    PRINT 'IX_UserRate_Provider already exists - skipped.';
GO
