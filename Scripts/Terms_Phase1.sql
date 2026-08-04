-- =============================================
-- Terms & Conditions Phase 1: versioned policies + acceptance audit
-- Cable EV Charging Station Management
-- =============================================
-- 1. TermsVersion            — policy documents in the DB (AR + EN), scoped by
--                              RoleId (null = all roles), SystemVersion display
--                              string, one active per role scope
-- 2. UserTermsAcceptance     — immutable who-accepted-what-when audit trail
-- 3. UserAccount             — denormalized AcceptedTermsVersionId + TermsAcceptedAt
--
-- Run on db_ab1977_cableproduction BEFORE deploying the build that contains
-- the terms feature. Idempotent: safe to re-run. No content is seeded — the
-- admin publishes the first version through the portal.
-- =============================================

-- 1. TermsVersion
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'TermsVersion')
BEGIN
    CREATE TABLE [dbo].[TermsVersion] (
        [Id]            INT IDENTITY(1,1) NOT NULL,
        [SystemVersion] NVARCHAR(20)      NOT NULL,
        [RoleId]        INT               NULL,
        [ContentEn]     NVARCHAR(MAX)     NOT NULL,
        [ContentAr]     NVARCHAR(MAX)     NOT NULL,
        [EffectiveFrom] DATETIME          NOT NULL,
        [IsActive]      BIT               NOT NULL DEFAULT 0,
        [CreatedAt]     DATETIME          NOT NULL DEFAULT GETUTCDATE(),
        [CreatedBy]     INT               NULL,
        [ModifiedAt]    DATETIME          NULL,
        [ModifiedBy]    INT               NULL,
        [IsDeleted]     BIT               NOT NULL DEFAULT 0,
        CONSTRAINT [PK_TermsVersion] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_TermsVersion_Role] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[Role] ([Id])
    );

    -- One active version per role scope (single NULL allowed = one general policy)
    CREATE UNIQUE NONCLUSTERED INDEX [UX_TermsVersion_ActivePerRole]
        ON [dbo].[TermsVersion] ([RoleId]) WHERE [IsActive] = 1 AND [IsDeleted] = 0;

    PRINT 'Created table: TermsVersion';
END
GO

-- 2. UserTermsAcceptance
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'UserTermsAcceptance')
BEGIN
    CREATE TABLE [dbo].[UserTermsAcceptance] (
        [Id]             INT IDENTITY(1,1) NOT NULL,
        [UserId]         INT      NOT NULL,
        [TermsVersionId] INT      NOT NULL,
        [AcceptedAt]     DATETIME NOT NULL,
        CONSTRAINT [PK_UserTermsAcceptance] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_UserTermsAcceptance_UserAccount] FOREIGN KEY ([UserId]) REFERENCES [dbo].[UserAccount] ([Id]),
        CONSTRAINT [FK_UserTermsAcceptance_TermsVersion] FOREIGN KEY ([TermsVersionId]) REFERENCES [dbo].[TermsVersion] ([Id])
    );

    CREATE UNIQUE NONCLUSTERED INDEX [UX_UserTermsAcceptance_User_Version]
        ON [dbo].[UserTermsAcceptance] ([UserId], [TermsVersionId]);
    CREATE NONCLUSTERED INDEX [IX_UserTermsAcceptance_TermsVersionId]
        ON [dbo].[UserTermsAcceptance] ([TermsVersionId]);

    PRINT 'Created table: UserTermsAcceptance';
END
GO

-- 3. UserAccount denormalized columns
IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.UserAccount') AND name = 'AcceptedTermsVersionId')
BEGIN
    ALTER TABLE [dbo].[UserAccount] ADD [AcceptedTermsVersionId] INT NULL, [TermsAcceptedAt] DATETIME NULL;
    PRINT 'Added columns: UserAccount.AcceptedTermsVersionId, UserAccount.TermsAcceptedAt';
END
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_UserAccount_TermsVersion')
BEGIN
    ALTER TABLE [dbo].[UserAccount]
        ADD CONSTRAINT [FK_UserAccount_TermsVersion]
        FOREIGN KEY ([AcceptedTermsVersionId]) REFERENCES [dbo].[TermsVersion] ([Id]);
    PRINT 'Added FK: FK_UserAccount_TermsVersion';
END
GO
