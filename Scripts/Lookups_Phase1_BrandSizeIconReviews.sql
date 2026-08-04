-- =============================================
-- Lookups Phase 1: ChargerBrand lookup, CarType icon,
--                  CarModelSize lookup, station reviews
-- Cable EV Charging Station Management
-- =============================================
-- 1. ChargerBrand lookup table (replaces free-text ChargingPoint.ChargerBrand)
--    + ChargingPointChargerBrand junction (M2M with per-brand Count),
--    seeded/backfilled from existing data, then the free-text column is DROPPED
-- 2. CarType.Icon column (logo icon)
-- 3. CarModelSize lookup (SUV, Hatchback, ...) + CarModel.SizeId FK
-- 4. Rate.Comment column (station reviews)
--
-- >>> RUN AGAINST THE DEV DATABASE ONLY — NOT PRODUCTION <<<
-- Idempotent: safe to re-run.
-- =============================================

-- 1a. ChargerBrand lookup table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ChargerBrand')
BEGIN
    CREATE TABLE [dbo].[ChargerBrand] (
        [Id]   INT IDENTITY(1,1) NOT NULL,
        [Name] NVARCHAR(100)     NOT NULL,
        CONSTRAINT [PK_ChargerBrand] PRIMARY KEY CLUSTERED ([Id] ASC)
    );

    CREATE UNIQUE NONCLUSTERED INDEX [UX_ChargerBrand_Name]
        ON [dbo].[ChargerBrand] ([Name]);

    PRINT 'Created table: ChargerBrand';
END
GO

-- 1b. Seed brands from existing distinct free-text values
--     (dynamic SQL: the free-text column is dropped in 1e, so a re-run must not
--      reference it directly or the batch would fail to compile)
IF COL_LENGTH('dbo.ChargingPoint', 'ChargerBrand') IS NOT NULL
EXEC sp_executesql N'
INSERT INTO [dbo].[ChargerBrand] ([Name])
SELECT DISTINCT LTRIM(RTRIM(CP.ChargerBrand))
FROM [dbo].[ChargingPoint] CP
WHERE CP.ChargerBrand IS NOT NULL
  AND LTRIM(RTRIM(CP.ChargerBrand)) <> ''''
  AND NOT EXISTS (SELECT 1 FROM [dbo].[ChargerBrand] B
                  WHERE B.Name = LTRIM(RTRIM(CP.ChargerBrand)));';
GO

-- 1c. Junction table: ChargingPoint <-> ChargerBrand (many-to-many with a
--     per-brand charger Count, e.g. 4x ABB + 3x Teison at one station)
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ChargingPointChargerBrand')
BEGIN
    CREATE TABLE [dbo].[ChargingPointChargerBrand] (
        [Id]              INT IDENTITY(1,1) NOT NULL,
        [ChargingPointId] INT NOT NULL,
        [ChargerBrandId]  INT NOT NULL,
        [Count]           INT NOT NULL DEFAULT 1,
        CONSTRAINT [PK_ChargingPointChargerBrand] PRIMARY KEY CLUSTERED ([Id] ASC),
        CONSTRAINT [FK_ChargingPointChargerBrand_ChargingPoint]
            FOREIGN KEY ([ChargingPointId]) REFERENCES [dbo].[ChargingPoint] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ChargingPointChargerBrand_ChargerBrand]
            FOREIGN KEY ([ChargerBrandId]) REFERENCES [dbo].[ChargerBrand] ([Id])
    );
    CREATE UNIQUE NONCLUSTERED INDEX [UX_ChargingPointChargerBrand_Point_Brand]
        ON [dbo].[ChargingPointChargerBrand] ([ChargingPointId], [ChargerBrandId]);
    PRINT 'Created table: ChargingPointChargerBrand';
END
GO

-- 1d. Migrate legacy free-text brands into the junction (count = station's
--     ChargersCount, since a single-brand station's chargers are all that brand)
IF COL_LENGTH('dbo.ChargingPoint', 'ChargerBrand') IS NOT NULL
EXEC sp_executesql N'
INSERT INTO [dbo].[ChargingPointChargerBrand] ([ChargingPointId], [ChargerBrandId], [Count])
SELECT CP.Id, B.Id, COALESCE(NULLIF(CP.ChargersCount, 0), 1)
FROM [dbo].[ChargingPoint] CP
JOIN [dbo].[ChargerBrand] B ON B.Name = LTRIM(RTRIM(CP.ChargerBrand))
WHERE CP.ChargerBrand IS NOT NULL
  AND LTRIM(RTRIM(CP.ChargerBrand)) <> ''''
  AND NOT EXISTS (SELECT 1 FROM [dbo].[ChargingPointChargerBrand] J
                  WHERE J.ChargingPointId = CP.Id AND J.ChargerBrandId = B.Id);';
GO

-- 1e. Drop the legacy free-text column — the junction is now the only brand
--     storage. Runs AFTER 1b/1d have preserved its data. (The API no longer
--     reads or writes ChargingPoint.ChargerBrand.)
IF COL_LENGTH('dbo.ChargingPoint', 'ChargerBrand') IS NOT NULL
BEGIN
    ALTER TABLE [dbo].[ChargingPoint] DROP COLUMN [ChargerBrand];
    PRINT 'Dropped column: ChargingPoint.ChargerBrand (junction is source of truth)';
END
GO

-- 2. CarType.Icon
IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.CarType') AND name = 'Icon')
BEGIN
    ALTER TABLE [dbo].[CarType] ADD [Icon] NVARCHAR(500) NULL;
    PRINT 'Added column: CarType.Icon';
END
GO

-- 3a. CarModelSize lookup table
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'CarModelSize')
BEGIN
    CREATE TABLE [dbo].[CarModelSize] (
        [Id]   INT IDENTITY(1,1) NOT NULL,
        [Name] NVARCHAR(50)      NOT NULL,
        CONSTRAINT [PK_CarModelSize] PRIMARY KEY CLUSTERED ([Id] ASC)
    );
    PRINT 'Created table: CarModelSize';
END
GO

-- 3b. Seed sizes
INSERT INTO [dbo].[CarModelSize] ([Name])
SELECT V.Name
FROM (VALUES ('SUV'), ('Hatchback'), ('Sedan'), ('Crossover'), ('Coupe'), ('Pickup'), ('Van')) AS V(Name)
WHERE NOT EXISTS (SELECT 1 FROM [dbo].[CarModelSize] S WHERE S.Name = V.Name);
GO

-- 3c. CarModel.SizeId FK
IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.CarModel') AND name = 'SizeId')
BEGIN
    ALTER TABLE [dbo].[CarModel] ADD [SizeId] INT NULL;
    PRINT 'Added column: CarModel.SizeId';
END
GO

IF NOT EXISTS (SELECT * FROM sys.foreign_keys WHERE name = 'FK_CarModel_CarModelSize')
BEGIN
    ALTER TABLE [dbo].[CarModel]
        ADD CONSTRAINT [FK_CarModel_CarModelSize]
        FOREIGN KEY ([SizeId]) REFERENCES [dbo].[CarModelSize] ([Id]);
    PRINT 'Added FK: FK_CarModel_CarModelSize';
END
GO

-- 4. Rate.Comment (station reviews)
IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID('dbo.Rate') AND name = 'Comment')
BEGIN
    ALTER TABLE [dbo].[Rate] ADD [Comment] NVARCHAR(1000) NULL;
    PRINT 'Added column: Rate.Comment';
END
GO
