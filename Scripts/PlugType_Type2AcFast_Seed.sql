/* =============================================================================
   Cable — Seed plug type "Type 2 AC (Fast)"  (Sprint 5 §6)
   -----------------------------------------------------------------------------
   The Flutter apps map plug types to icons/labels BY NUMERIC ID and already ship
   with this plug hard-coded as id 23. The next identity value on both dev and
   production is 22, so a plain INSERT would land on 22 and the plug would never
   render in either app. The id is therefore forced to 23 with IDENTITY_INSERT.
   The resulting gap at 22 is harmless — ids 3, 4 and 5 are already gaps.

   Idempotent: skips if id 23 or the serial 'AC TYPE 2 FAST' already exists.
   Verified 2026-09-22: dev and production both at IDENT_CURRENT = 21, id 23
   free, serial absent.

   Run on dev first, then production.
   ============================================================================= */

SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
SET NOCOUNT ON;
SET XACT_ABORT ON;

DECLARE @Id     INT           = 23;
DECLARE @Name   NVARCHAR(100) = N'Type 2 AC (Fast)';
DECLARE @Serial NVARCHAR(100) = N'AC TYPE 2 FAST';
DECLARE @Family NVARCHAR(50)  = N'EURO';

IF EXISTS (SELECT 1 FROM dbo.PlugType WHERE Id = @Id)
BEGIN
    PRINT CONCAT('PlugType id ', @Id, ' already exists - skipped.');
    SELECT Id, Name, SerialNumber, PlugTypeFamily, IsDeleted FROM dbo.PlugType WHERE Id = @Id;
END
ELSE IF EXISTS (SELECT 1 FROM dbo.PlugType WHERE SerialNumber = @Serial AND IsDeleted = 0)
BEGIN
    PRINT CONCAT('Serial ''', @Serial, ''' already exists under a different id - NOT inserted. Review:');
    SELECT Id, Name, SerialNumber, PlugTypeFamily, IsDeleted FROM dbo.PlugType WHERE SerialNumber = @Serial;
END
ELSE
BEGIN
    BEGIN TRAN;
    SET IDENTITY_INSERT dbo.PlugType ON;

    INSERT INTO dbo.PlugType (Id, Name, SerialNumber, PlugTypeFamily, IsDeleted, CreatedAt, CreatedBy)
    VALUES (@Id, @Name, @Serial, @Family, 0, GETUTCDATE(), NULL);

    SET IDENTITY_INSERT dbo.PlugType OFF;
    COMMIT TRAN;

    PRINT CONCAT('Inserted PlugType id ', @Id, ': ', @Name, ' [', @Serial, ']');
    SELECT Id, Name, SerialNumber, PlugTypeFamily, IsDeleted FROM dbo.PlugType WHERE Id = @Id;
END
GO
