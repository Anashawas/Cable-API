/* =============================================================================
   Cable — Normalize UserAccount.City to 12 canonical Jordanian cities
   -----------------------------------------------------------------------------
   Canonical set:
       Amman, Zarqa, Irbid, Salt, Mafraq, Madaba,
       Jerash, Ajloun, Karak, Tafilah, Maan, Aqaba

   This script:
     1) Adds a backup column dbo.UserAccount.OriginalCity (one-time)
     2) Snapshots the current City -> OriginalCity (one-time; idempotent guard)
     3) Maps every recognized variant (governorate name in AR/EN, common
        spelling variants, well-known sub-towns) to one canonical city
     4) Defaults NULL / empty City values to 'Amman'
     5) Optionally sets unmappable cities (foreign or unrecognized) to NULL
        (off by default — review the unmapped report first)
     6) Reports before/after counts and the list of unmapped distinct values

   Safety:
     - Wrapped in a transaction with XACT_ABORT ON.
     - Re-running won't lose the original backup (NOT EXISTS guard).
     - Rollback (full revert):
           UPDATE dbo.UserAccount SET City = OriginalCity;

   IMPORTANT — TEST ON DEV FIRST.
   ============================================================================= */

SET NOCOUNT ON;
SET XACT_ABORT ON;

-- ----------------------------------------------------------------------------
-- 1) Add backup column (idempotent)
-- ----------------------------------------------------------------------------
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE Name = N'OriginalCity' AND Object_ID = Object_ID(N'dbo.UserAccount'))
BEGIN
    ALTER TABLE dbo.UserAccount ADD OriginalCity NVARCHAR(100) NULL;
END;
GO

-- ----------------------------------------------------------------------------
-- 2) Backup current City -> OriginalCity (one-time only)
--    Must be a separate batch because OriginalCity was just added above.
-- ----------------------------------------------------------------------------
UPDATE dbo.UserAccount
SET    OriginalCity = City
WHERE  OriginalCity IS NULL AND City IS NOT NULL;
GO

-- ============================================================================
-- Main batch — all variables, temp tables, and the transaction live here.
-- Do NOT add GO statements below this line.
-- ============================================================================
SET NOCOUNT ON;
SET XACT_ABORT ON;

-- 0 = leave unmapped values unchanged (safer; recommended for first run).
-- 1 = NULL out every value that isn't recognized as one of the 12 cities.
DECLARE @NullOutUnmapped BIT = 0;

-- ----------------------------------------------------------------------------
-- 3) Build mapping table (normalized lookup -> canonical city)
--    Normalization at compare time: TRIM, LOWER, strip Arabic comma N'،'
--    and Latin comma. Arabic letters compare exactly (no case).
-- ----------------------------------------------------------------------------
IF OBJECT_ID('tempdb..#Map') IS NOT NULL DROP TABLE #Map;
CREATE TABLE #Map (
    NormVariant NVARCHAR(100) COLLATE Latin1_General_CI_AI NOT NULL PRIMARY KEY,
    Canonical   NVARCHAR(40)  NOT NULL
);

-- ===== AMMAN ================================================================
INSERT INTO #Map (NormVariant, Canonical) VALUES
(N'amman',                 N'Amman'),
(N'amman governorate',     N'Amman'),
(N'greater amman',         N'Amman'),
-- N'ammán' is matched by 'amman' under accent-insensitive collation; no separate row needed
(N'amman,',                N'Amman'),
(N'محافظة العاصمة',        N'Amman'),
(N'محافظة عمان',           N'Amman'),
(N'محافظة عمّان',           N'Amman'),
(N'عمان',                   N'Amman'),
(N'عمّان',                   N'Amman'),
(N'منطقة الرابية',          N'Amman'),
(N'وادي السير',             N'Amman'),
(N'سحاب',                   N'Amman'),
(N'ناعور',                  N'Amman'),
(N'الجيزة',                 N'Amman'),
(N'الموقر',                 N'Amman'),
(N'安曼',                   N'Amman'); -- "Amman" in Chinese

-- ===== ZARQA ================================================================
INSERT INTO #Map (NormVariant, Canonical) VALUES
(N'zarqa',                   N'Zarqa'),
(N'az-zarqa',                N'Zarqa'),
(N'zarqa governorate',       N'Zarqa'),
(N'al zarqa governorate',    N'Zarqa'),
(N'الزرقاء',                  N'Zarqa'),
(N'محافظة الزرقاء',          N'Zarqa'),
(N'الرصيفة',                  N'Zarqa'),
(N'russeifa',                N'Zarqa'),
(N'الهاشمية',                 N'Zarqa'),
(N'al hashemiya',            N'Zarqa'),
(N'al-hashemiya',            N'Zarqa'),
(N'hashemeyeh',              N'Zarqa'),
(N'الضليل',                   N'Zarqa'),
(N'dhlail',                  N'Zarqa'),
(N'السخنة',                   N'Zarqa'),
(N'as-sukhnah',              N'Zarqa'),
(N'الأزرق',                   N'Zarqa'),
(N'azraq',                   N'Zarqa'),
(N'al-azraq al-shamaly',     N'Zarqa'),
(N'azraq ed duruz',          N'Zarqa'),
(N'بيرين',                    N'Zarqa');

-- ===== IRBID ================================================================
INSERT INTO #Map (NormVariant, Canonical) VALUES
(N'irbid',                   N'Irbid'),
(N'irbid governorate',       N'Irbid'),
(N'greater irbid',           N'Irbid'),
(N'إربد',                     N'Irbid'),
(N'محافظة إربد',             N'Irbid'),
(N'иирбид',                  N'Irbid'),
(N'الرمثا',                   N'Irbid'),
(N'ar-ramtha',               N'Irbid'),
(N'ar ramtha',               N'Irbid'),
(N'الحصن',                    N'Irbid'),
-- N'الحصن‎' (with LRM) matched by 'الحصن' under CI_AI
(N'husn',                    N'Irbid'),
(N'al-husun',                N'Irbid'),
(N'إيدون',                    N'Irbid'),
(N'aydoun',                  N'Irbid'),
(N'دير أبي سعيد',            N'Irbid'),
(N'deir abu said',           N'Irbid'),
(N'der abi saeed',           N'Irbid'),
(N'النعيمة',                  N'Irbid'),
(N'an-nuayyimah',            N'Irbid'),
(N'الصريح',                   N'Irbid'),
(N'as-sarih',                N'Irbid'),
(N'كفر أسد',                 N'Irbid'),
(N'kufr asad',               N'Irbid'),
(N'كفر سوم',                 N'Irbid'),
(N'كفرعوان',                 N'Irbid'),
(N'kufr ''awan',             N'Irbid'),
(N'kufr jayes',              N'Irbid'),
(N'كفر جايز',                N'Irbid'),
(N'كفر راكب',                N'Irbid'),
(N'يبلا (إربد)',             N'Irbid'),
(N'بيت يافا',                N'Irbid'),
(N'bayt yafa',               N'Irbid'),
(N'ملكا',                    N'Irbid'),
(N'malka',                   N'Irbid'),
(N'بنيكنانه',                N'Irbid'),
(N'سحم',                     N'Irbid'),
(N'sheeri',                  N'Irbid'),
(N'حواره',                   N'Irbid'),
(N'مرو',                     N'Irbid'),
(N'شطنا',                    N'Irbid'),
(N'حبراص',                   N'Irbid'),
(N'صما',                     N'Irbid'),
(N'طبقة فحل',                N'Irbid'),
(N'وقاص',                    N'Irbid'),
-- N'وقاص‎' (with LRM) matched by 'وقاص' under CI_AI
(N'عنبة',                    N'Irbid'),
(N'سوم',                     N'Irbid'),
(N'حرثا',                    N'Irbid'),
(N'كتم',                     N'Irbid'),
(N'المزار الشمالية',         N'Irbid'),
(N'north shuna',             N'Irbid'),
(N'الشجرة',                  N'Irbid'),
(N'ash shajarah',            N'Irbid'),
(N'ash-shajarah',            N'Irbid'),
(N'الطرة',                   N'Irbid');
-- N'كفر سوم' already inserted above

-- ===== SALT (Balqa) =========================================================
INSERT INTO #Map (NormVariant, Canonical) VALUES
(N'salt',                    N'Salt'),
(N'salt (balqa)',            N'Salt'),
(N'as-salt',                 N'Salt'),
(N'balqa governorate',       N'Salt'),
(N'البلقاء',                  N'Salt'),
(N'محافظة البلقاء',          N'Salt'),
(N'السلط',                   N'Salt'),
(N'عين الباشا',              N'Salt'),
(N'ein al-basha',            N'Salt'),
(N'صافوط',                   N'Salt'),
(N'safut',                   N'Salt'),
(N'ماحص',                    N'Salt'),
(N'زي',                      N'Salt'),
(N'الكرامة',                 N'Salt'),
(N'karameh',                 N'Salt'),
(N'الشونة الجنوبية',         N'Salt'),
(N'دير علا',                 N'Salt'),
(N'dayr ''allah',            N'Salt'),
(N'البقعة',                  N'Salt'),
(N'جوفة الكفرين',            N'Salt'),
(N'داميه',                   N'Salt');

-- ===== MAFRAQ ===============================================================
INSERT INTO #Map (NormVariant, Canonical) VALUES
(N'mafraq',                  N'Mafraq'),
(N'al mafraq',               N'Mafraq'),
(N'mafraq governorate',      N'Mafraq'),
(N'المفرق',                  N'Mafraq'),
(N'محافظة المفرق',           N'Mafraq'),
(N'الحنو',                   N'Mafraq'),
(N'al hanou',                N'Mafraq'),
(N'صبحا',                    N'Mafraq'),
(N'بلعما',                   N'Mafraq'),
(N'balama',                  N'Mafraq'),
(N'رحاب',                    N'Mafraq'),
(N'ام الجمال',               N'Mafraq'),
(N'أم الجمال',               N'Mafraq'),
(N'umm al-quttayn',          N'Mafraq'),
(N'جابر السرحان',            N'Mafraq'),
(N'safawi',                  N'Mafraq'),
(N'الصفاوي',                 N'Mafraq'),
(N'الرويشد',                 N'Mafraq'),
(N'umm sleih',               N'Mafraq'),
(N'الخالدية',                N'Mafraq'),
(N'مغاير مهنا',              N'Mafraq'),
(N'zaatari village',         N'Mafraq');

-- ===== MADABA ===============================================================
INSERT INTO #Map (NormVariant, Canonical) VALUES
(N'madaba',                  N'Madaba'),
(N'madaba governorate',      N'Madaba'),
(N'مادبا',                   N'Madaba'),
(N'محافظة مادبا',            N'Madaba'),
(N'مأدبا',                   N'Madaba'),
(N'ذيبان',                   N'Madaba'),
(N'مكاور',                   N'Madaba'),
(N'khirbet al-mukhayyat',    N'Madaba');

-- ===== JERASH ===============================================================
INSERT INTO #Map (NormVariant, Canonical) VALUES
(N'jerash',                  N'Jerash'),
(N'jerash governorate',      N'Jerash'),
(N'jarash',                  N'Jerash'),
(N'جرش',                     N'Jerash'),
(N'محافظة جرش',              N'Jerash'),
(N'ساكب',                    N'Jerash'),
(N'برما',                    N'Jerash'),
(N'علان',                    N'Jerash');

-- ===== AJLOUN ===============================================================
INSERT INTO #Map (NormVariant, Canonical) VALUES
(N'ajlun',                   N'Ajloun'),
(N'ajloun',                  N'Ajloun'),
(N'ajloun governorate',      N'Ajloun'),
(N'عجلون',                   N'Ajloun'),
(N'محافظة عجلون',            N'Ajloun'),
(N'كفرنجة',                  N'Ajloun'),
(N'عنجرة',                   N'Ajloun'),
(N'anjara',                  N'Ajloun'),
(N'صخره',                    N'Ajloun'),
(N'خربة الوهادنة',           N'Ajloun');

-- ===== KARAK ================================================================
INSERT INTO #Map (NormVariant, Canonical) VALUES
(N'karak',                   N'Karak'),
(N'karak governorate',       N'Karak'),
(N'al-karak',                N'Karak'),
(N'al karak governorate',    N'Karak'),
(N'الكرك',                   N'Karak'),
(N'محافظة الكرك',            N'Karak'),
(N'مؤتة',                    N'Karak'),
(N'mu''tah',                 N'Karak'),
(N'mu'' tah',                N'Karak'),
(N'ربا',                     N'Karak'),
(N'rabba',                   N'Karak'),
(N'al-qatrana',              N'Karak'),
(N'القطرانة',                N'Karak'),
(N'المنشية',                 N'Karak'),
(N'al-manshiyah',            N'Karak'),
(N'ضرار',                    N'Karak');

-- ===== TAFILAH ==============================================================
INSERT INTO #Map (NormVariant, Canonical) VALUES
(N'tafilah',                 N'Tafilah'),
(N'tafielah',                N'Tafilah'),
(N'tafilah governorate',     N'Tafilah'),
(N'at-tafila',               N'Tafilah'),
(N'الطفيلة',                 N'Tafilah'),
(N'محافظة الطفيلة',          N'Tafilah'),
(N'الطفيل',                  N'Tafilah'),
(N'الحسا',                   N'Tafilah'),
(N'hasa',                    N'Tafilah'),
(N'dhana',                   N'Tafilah'),
(N'ضانا',                    N'Tafilah'),
(N'جرف الدراويش',            N'Tafilah'),
(N'jurf ed-darawish',        N'Tafilah');

-- ===== MAAN =================================================================
INSERT INTO #Map (NormVariant, Canonical) VALUES
(N'maan',                    N'Maan'),
(N'ma-an',                   N'Maan'),
(N'ma''an',                  N'Maan'),
(N'ma''an governorate',      N'Maan'),
(N'معان',                    N'Maan'),
(N'محافظة معان',             N'Maan'),
(N'وادي موسى',               N'Maan'),
(N'wadi musa',               N'Maan'),
(N'الحسينية',                N'Maan'),
(N'al-husainya',             N'Maan'),
(N'الراشدية',                N'Maan'),
(N'rashidiyah',              N'Maan'),
(N'الشوبك',                  N'Maan'),
(N'shobak',                  N'Maan');

-- ===== AQABA ================================================================
INSERT INTO #Map (NormVariant, Canonical) VALUES
(N'aqaba',                   N'Aqaba'),
(N'al-aqabah',               N'Aqaba'),
(N'aqaba governorate',       N'Aqaba'),
(N'العقبة',                  N'Aqaba'),
(N'محافظة العقبة',           N'Aqaba'),
(N'وادي رم',                 N'Aqaba'),
(N'قرية وادي رم',            N'Aqaba'),
(N'wadi rum',                N'Aqaba'),
(N'الديسة',                  N'Aqaba'),
(N'القويرة',                 N'Aqaba'),
(N'el quweira',              N'Aqaba');

-- ----------------------------------------------------------------------------
-- 4) Build normalized snapshot of UserAccount.City for joining
-- ----------------------------------------------------------------------------
IF OBJECT_ID('tempdb..#UA') IS NOT NULL DROP TABLE #UA;

SELECT
    Id,
    City AS OriginalCity,
    -- normalize: trim, lower, strip Arabic + Latin commas
    LOWER(LTRIM(RTRIM(
        REPLACE(REPLACE(City COLLATE Latin1_General_CI_AI, N'،', N''), N',', N'')
    ))) AS NormCity
INTO #UA
FROM dbo.UserAccount
WHERE City IS NOT NULL AND LTRIM(RTRIM(City)) <> '';

CREATE INDEX IX_UA_Norm ON #UA(NormCity);

-- ----------------------------------------------------------------------------
-- 5) Pre-update report
-- ----------------------------------------------------------------------------
DECLARE @Total       INT = (SELECT COUNT(*) FROM dbo.UserAccount);
DECLARE @NonEmpty    INT = (SELECT COUNT(*) FROM #UA);
DECLARE @NullEmpty   INT = (SELECT COUNT(*) FROM dbo.UserAccount
                            WHERE City IS NULL OR LTRIM(RTRIM(City)) = '');
DECLARE @Mappable    INT = (SELECT COUNT(*) FROM #UA u JOIN #Map m ON u.NormCity = m.NormVariant);
DECLARE @Unmappable  INT = @NonEmpty - @Mappable;

PRINT '----- Pre-update -----';
PRINT CONCAT(' Total rows                       : ', @Total);
PRINT CONCAT(' Rows with non-empty City         : ', @NonEmpty);
PRINT CONCAT(' Rows with NULL/empty City        : ', @NullEmpty, ' (will be defaulted to Amman)');
PRINT CONCAT(' Rows mappable to canonical       : ', @Mappable);
PRINT CONCAT(' Rows unmappable                  : ', @Unmappable);
PRINT CONCAT(' NullOutUnmapped flag             : ', @NullOutUnmapped);

-- ----------------------------------------------------------------------------
-- 6) Apply mapping inside a transaction
-- ----------------------------------------------------------------------------
BEGIN TRAN;

    -- 6a) Map known variants -> canonical city
    UPDATE u
    SET u.City = m.Canonical
    FROM dbo.UserAccount u
    JOIN #UA  s ON s.Id = u.Id
    JOIN #Map m ON m.NormVariant = s.NormCity
    WHERE u.City <> m.Canonical OR u.City IS NULL;

    DECLARE @UpdatedMapped INT = @@ROWCOUNT;
    PRINT CONCAT(' Rows updated to canonical        : ', @UpdatedMapped);

    -- 6b) Default NULL / empty City to 'Amman'
    UPDATE dbo.UserAccount
    SET    City = N'Amman'
    WHERE  City IS NULL OR LTRIM(RTRIM(City)) = '';

    DECLARE @UpdatedDefaulted INT = @@ROWCOUNT;
    PRINT CONCAT(' Rows defaulted to Amman          : ', @UpdatedDefaulted);

    -- 6c) Optionally NULL out unmappable values
    DECLARE @UpdatedNulled INT = 0;
    IF @NullOutUnmapped = 1
    BEGIN
        UPDATE u
        SET u.City = NULL
        FROM dbo.UserAccount u
        JOIN #UA s ON s.Id = u.Id
        LEFT JOIN #Map m ON m.NormVariant = s.NormCity
        WHERE m.NormVariant IS NULL;

        SET @UpdatedNulled = @@ROWCOUNT;
        PRINT CONCAT(' Rows set to NULL (unmappable)    : ', @UpdatedNulled);
    END

COMMIT TRAN;

-- ----------------------------------------------------------------------------
-- 7) Post-update report
-- ----------------------------------------------------------------------------
PRINT '';
PRINT '----- Post-update — canonical city distribution -----';
SELECT
    ISNULL(City, '(NULL)') AS City,
    COUNT(*)               AS Users
FROM dbo.UserAccount
GROUP BY City
ORDER BY Users DESC;

PRINT '';
PRINT '----- Unmapped distinct values still in City (for manual review) -----';
SELECT
    s.OriginalCity         AS RawValue,
    COUNT(*)               AS Users
FROM #UA s
LEFT JOIN #Map m ON m.NormVariant = s.NormCity
WHERE m.NormVariant IS NULL
GROUP BY s.OriginalCity
ORDER BY Users DESC;

PRINT '';
PRINT 'DONE.';

/* =============================================================================
   ROLLBACK (run only if you need to revert all rows to the pre-script values):

       BEGIN TRAN;
           UPDATE dbo.UserAccount
           SET    City = OriginalCity
           WHERE  OriginalCity IS NOT NULL;
       COMMIT TRAN;

   AFTER ROLLBACK you can also drop the backup column:

       ALTER TABLE dbo.UserAccount DROP COLUMN OriginalCity;
   ============================================================================= */
