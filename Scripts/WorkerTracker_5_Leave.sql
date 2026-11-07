-- Worker tracker, step 5 — leave (Docs/WorkerTracker-Plan.md §5):
--   * ProviderManager.AnnualLeaveDays / SickLeaveDays (defaults 14 / 14, owner-editable — decision 14).
--   * dbo.LeaveRequest: paid / unpaid / sick requests and their decisions; the sick attachment file name.
--   * Notification types leave_request (owner + ApproveLeave workers) and leave_decision (worker).
-- Idempotent: safe to run again. Apply to dev first; deploy the API build of this change only after the script.

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

IF COL_LENGTH('dbo.ProviderManager', 'AnnualLeaveDays') IS NULL
BEGIN
    ALTER TABLE dbo.ProviderManager ADD AnnualLeaveDays INT NOT NULL CONSTRAINT DF_ProviderManager_AnnualLeaveDays DEFAULT 14,
                                        SickLeaveDays   INT NOT NULL CONSTRAINT DF_ProviderManager_SickLeaveDays DEFAULT 14;
    PRINT 'Added ProviderManager.AnnualLeaveDays / SickLeaveDays';
END
ELSE PRINT 'SKIP ProviderManager leave columns exist';
GO

IF OBJECT_ID('dbo.LeaveRequest', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.LeaveRequest
    (
        Id                 INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_LeaveRequest PRIMARY KEY,
        ProviderManagerId  INT           NOT NULL,
        Kind               NVARCHAR(10)  NOT NULL,    -- paid | unpaid | sick
        FromDate           DATE          NOT NULL,
        ToDate             DATE          NOT NULL,
        DaysCounted        INT           NOT NULL,    -- off days and public holidays left out
        Reason             NVARCHAR(500) NULL,
        AttachmentFileName NVARCHAR(300) NULL,        -- sick: the medical report (private, owner only, deleted after a year)
        [Status]           NVARCHAR(20)  NOT NULL,    -- pending | approved | rejected | cancelled
        RequestedByUserId  INT           NOT NULL,
        DecidedByUserId    INT           NULL,
        DecidedAt          DATETIME2(0)  NULL,
        DecisionNote       NVARCHAR(500) NULL,
        CancelledByUserId  INT           NULL,
        CancelledAt        DATETIME2(0)  NULL,
        CreatedBy INT NULL, CreatedAt DATETIME NOT NULL, ModifiedBy INT NULL, ModifiedAt DATETIME NULL,
        IsDeleted          BIT           NOT NULL CONSTRAINT DF_LeaveRequest_IsDeleted DEFAULT 0,
        CONSTRAINT FK_LeaveRequest_ProviderManager FOREIGN KEY (ProviderManagerId) REFERENCES dbo.ProviderManager (Id)
    );
    CREATE INDEX IX_LeaveRequest_Worker_From ON dbo.LeaveRequest (ProviderManagerId, FromDate) WHERE [IsDeleted] = 0;
    PRINT 'Created dbo.LeaveRequest';
END
ELSE PRINT 'SKIP dbo.LeaveRequest exists';
GO

INSERT INTO dbo.NotificationType ([Name], [Description])
SELECT v.Name, v.Description
FROM (VALUES
    ('leave_request',  'A worker asked for leave (sent to the owner and ApproveLeave workers)'),
    ('leave_decision', 'A leave request was approved, rejected or cancelled (sent to the worker)')
) AS v(Name, Description)
WHERE NOT EXISTS (SELECT 1 FROM dbo.NotificationType t WHERE t.Name = v.Name);
PRINT CONCAT('Notification types added: ', @@ROWCOUNT);
GO
