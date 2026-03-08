-- =============================================
-- Cable EV - Password Reset Table
-- =============================================
-- This table stores password reset codes sent via email
-- for the "Forgot Password" functionality
-- =============================================

USE [db_ab1977_cableproduction];
GO

-- Create PasswordResets table
CREATE TABLE [dbo].[PasswordResets] (
    [Id] INT IDENTITY(1,1) NOT NULL,
    [UserId] INT NOT NULL,
    [Code] NVARCHAR(6) NOT NULL,
    [ExpiresAt] DATETIME2(7) NOT NULL,
    [IsUsed] BIT NOT NULL DEFAULT 0,
    [CreatedAt] DATETIME2(7) NOT NULL DEFAULT GETUTCDATE(),
    [UsedAt] DATETIME2(7) NULL,
    [FailedAttempts] INT NOT NULL DEFAULT 0,
    [IpAddress] NVARCHAR(45) NULL,

    CONSTRAINT [PK_PasswordResets] PRIMARY KEY CLUSTERED ([Id] ASC),
    CONSTRAINT [FK_PasswordResets_UserAccounts] FOREIGN KEY ([UserId])
        REFERENCES [dbo].[UserAccounts]([Id]) ON DELETE CASCADE
);
GO

-- Create indexes for performance
CREATE NONCLUSTERED INDEX [IX_PasswordResets_Code]
    ON [dbo].[PasswordResets]([Code] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_PasswordResets_UserId]
    ON [dbo].[PasswordResets]([UserId] ASC);
GO

CREATE NONCLUSTERED INDEX [IX_PasswordResets_ExpiresAt]
    ON [dbo].[PasswordResets]([ExpiresAt] ASC);
GO

-- Create index for finding active codes
CREATE NONCLUSTERED INDEX [IX_PasswordResets_Active]
    ON [dbo].[PasswordResets]([Code], [IsUsed], [ExpiresAt] ASC)
    INCLUDE ([UserId]);
GO

PRINT 'PasswordResets table created successfully!';
GO
