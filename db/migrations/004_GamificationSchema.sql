/* ============================================================================
   004_GamificationSchema.sql — the Gamification schema, from nothing
   ----------------------------------------------------------------------------
   Sparks (the app's currency), streaks, streak freezes and the shop: the loop
   that gets a child to open the app tomorrow. They play, they earn, they build
   a habit, and they spend what they earned protecting it.

   This script's only job is to stand up Gamification.* objects. It contains NO
   DDL for any other module.

   Cross-module references are plain columns with NO foreign key, by design —
   the same convention Assessment uses:
       LearnerWallets.UserId, SparkTransactions.UserId,
       LearnerItems.UserId, LearnerBoosts.UserId   → Users.Users.Id

   Safe to re-run: every object is created only if it is missing, and the seed
   rows are matched on their stable Code.
   ========================================================================== */

USE VoltDB;
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF SCHEMA_ID(N'Gamification') IS NULL
    EXEC(N'CREATE SCHEMA Gamification');
GO


/* ============================================================================
   LearnerWallets — one row per learner: what they hold and what they are
   protecting. Created the first time they earn anything.
   ========================================================================== */

IF OBJECT_ID(N'Gamification.LearnerWallets') IS NULL
BEGIN
    CREATE TABLE Gamification.LearnerWallets
    (
        -- Cross-module, no FK (see header).
        UserId              UNIQUEIDENTIFIER    NOT NULL,
        SparksBalance       INT                 NOT NULL
            CONSTRAINT DF_LearnerWallets_SparksBalance DEFAULT (0),
        -- Never goes down, so spending cannot erase a record.
        LifetimeSparks      INT                 NOT NULL
            CONSTRAINT DF_LearnerWallets_LifetimeSparks DEFAULT (0),
        CurrentStreakDays   INT                 NOT NULL
            CONSTRAINT DF_LearnerWallets_CurrentStreakDays DEFAULT (0),
        LongestStreakDays   INT                 NOT NULL
            CONSTRAINT DF_LearnerWallets_LongestStreakDays DEFAULT (0),
        -- A DATE, not a timestamp: a streak counts days, and storing the instant
        -- invited comparisons that made "yesterday" depend on the time of day.
        LastActivityOn      DATE                NULL,
        -- Each one covers one missed day, spent automatically on the child's
        -- return. Capped by the application (two by default).
        StreakFreezes       TINYINT             NOT NULL
            CONSTRAINT DF_LearnerWallets_StreakFreezes DEFAULT (0),
        StreakFreezesUsed   INT                 NOT NULL
            CONSTRAINT DF_LearnerWallets_StreakFreezesUsed DEFAULT (0),
        CreatedAt           DATETIME2(3)        NOT NULL
            CONSTRAINT DF_LearnerWallets_CreatedAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt           DATETIME2(3)        NOT NULL
            CONSTRAINT DF_LearnerWallets_UpdatedAt DEFAULT (SYSUTCDATETIME()),
        -- Makes the read-modify-write of a reward safe: two activities finishing
        -- at the same instant no longer lose one set of Sparks.
        RowVersion          ROWVERSION          NOT NULL,

        CONSTRAINT PK_LearnerWallets PRIMARY KEY CLUSTERED (UserId),
        -- A balance reaches zero but never goes below it, whatever a bug in the
        -- purchase path does.
        CONSTRAINT CK_LearnerWallets_SparksBalance CHECK (SparksBalance >= 0),
        CONSTRAINT CK_LearnerWallets_LifetimeSparks CHECK (LifetimeSparks >= 0),
        CONSTRAINT CK_LearnerWallets_CurrentStreak CHECK (CurrentStreakDays >= 0),
        CONSTRAINT CK_LearnerWallets_LongestStreak CHECK (LongestStreakDays >= CurrentStreakDays)
    );
END
GO


/* ============================================================================
   SparkTransactions — the ledger behind the balance, and the thing that makes
   a reward payable exactly once.
   ========================================================================== */

IF OBJECT_ID(N'Gamification.SparkTransactions') IS NULL
BEGIN
    CREATE TABLE Gamification.SparkTransactions
    (
        Id              BIGINT IDENTITY(1,1)    NOT NULL,
        UserId          UNIQUEIDENTIFIER        NOT NULL,
        -- Positive when earned, negative when spent. Never zero.
        Amount          INT                     NOT NULL,
        Reason          NVARCHAR(40)            NOT NULL,
        -- What it was for: "attempt:42", "lesson:5", "streak:14". With Reason it
        -- identifies the event, so the event can only ever pay once. NULL for a
        -- movement with no natural identity (a purchase, an adjustment).
        ReferenceKey    NVARCHAR(100)           NULL,
        BalanceAfter    INT                     NOT NULL,
        CreatedAt       DATETIME2(3)            NOT NULL
            CONSTRAINT DF_SparkTransactions_CreatedAt DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_SparkTransactions PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_SparkTransactions_LearnerWallets FOREIGN KEY (UserId)
            REFERENCES Gamification.LearnerWallets (UserId) ON DELETE CASCADE,
        CONSTRAINT CK_SparkTransactions_AmountNotZero CHECK (Amount <> 0),
        CONSTRAINT CK_SparkTransactions_BalanceAfter CHECK (BalanceAfter >= 0)
    );

    -- THE idempotency rule. A replayed submit, a retried request or two app
    -- instances racing all try to write the same (learner, reason, reference)
    -- row; exactly one succeeds and the rest are recognised as duplicates.
    -- FILTERED, because a movement without a reference has no identity to be
    -- unique on — and a plain unique index would treat every NULL as equal and
    -- allow only one purchase per learner in the whole database.
    CREATE UNIQUE INDEX UQ_SparkTransactions_UserId_Reason_ReferenceKey
        ON Gamification.SparkTransactions (UserId, Reason, ReferenceKey)
        WHERE ReferenceKey IS NOT NULL;

    CREATE INDEX IX_SparkTransactions_UserId_CreatedAt
        ON Gamification.SparkTransactions (UserId, CreatedAt);
END
GO


/* ============================================================================
   ShopItems — what Sparks can be spent on. The catalogue is small enough that
   its two languages live in columns rather than a translation table.
   ========================================================================== */

IF OBJECT_ID(N'Gamification.ShopItems') IS NULL
BEGIN
    CREATE TABLE Gamification.ShopItems
    (
        Id              INT IDENTITY(1,1)   NOT NULL,
        -- Stable machine name, so code never depends on an identity value.
        Code            NVARCHAR(40)        NOT NULL,
        Kind            NVARCHAR(20)        NOT NULL,
        NameAr          NVARCHAR(100)       NOT NULL,
        NameEn          NVARCHAR(100)       NOT NULL,
        DescriptionAr   NVARCHAR(500)       NULL,
        DescriptionEn   NVARCHAR(500)       NULL,
        PriceSparks     INT                 NOT NULL,
        -- How many a child may hold at once, or NULL for no limit. Two freezes is
        -- the deliberate ceiling: enough to survive a bad week, not enough to buy
        -- ten and disappear for ten days.
        MaxOwned        TINYINT             NULL,
        BoostMultiplier TINYINT             NULL,
        BoostMinutes    INT                 NULL,
        ImageUrl        NVARCHAR(500)       NULL,
        IsActive        BIT                 NOT NULL
            CONSTRAINT DF_ShopItems_IsActive DEFAULT (1),
        SortOrder       SMALLINT            NOT NULL
            CONSTRAINT DF_ShopItems_SortOrder DEFAULT (0),

        CONSTRAINT PK_ShopItems PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_ShopItems_Code UNIQUE (Code),
        CONSTRAINT CK_ShopItems_Kind CHECK (Kind IN ('StreakFreeze', 'Avatar', 'Boost')),
        CONSTRAINT CK_ShopItems_Price CHECK (PriceSparks >= 0),
        -- A boost that multiplies by nothing, or runs for no time, is not a
        -- boost; a non-boost carrying either is a mis-seeded row.
        CONSTRAINT CK_ShopItems_BoostIsComplete
            CHECK ((Kind = 'Boost' AND BoostMultiplier > 1 AND BoostMinutes > 0)
                OR (Kind <> 'Boost' AND BoostMultiplier IS NULL AND BoostMinutes IS NULL))
    );
END
GO


/* ============================================================================
   LearnerItems — what a learner owns.
   ========================================================================== */

IF OBJECT_ID(N'Gamification.LearnerItems') IS NULL
BEGIN
    CREATE TABLE Gamification.LearnerItems
    (
        Id          BIGINT IDENTITY(1,1)    NOT NULL,
        UserId      UNIQUEIDENTIFIER        NOT NULL,
        ShopItemId  INT                     NOT NULL,
        Quantity    INT                     NOT NULL,
        -- Cosmetics only: several owned, at most one worn.
        IsEquipped  BIT                     NOT NULL
            CONSTRAINT DF_LearnerItems_IsEquipped DEFAULT (0),
        AcquiredAt  DATETIME2(3)            NOT NULL
            CONSTRAINT DF_LearnerItems_AcquiredAt DEFAULT (SYSUTCDATETIME()),
        UpdatedAt   DATETIME2(3)            NOT NULL
            CONSTRAINT DF_LearnerItems_UpdatedAt DEFAULT (SYSUTCDATETIME()),

        CONSTRAINT PK_LearnerItems PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT UQ_LearnerItems_UserId_ShopItemId UNIQUE (UserId, ShopItemId),
        CONSTRAINT FK_LearnerItems_LearnerWallets FOREIGN KEY (UserId)
            REFERENCES Gamification.LearnerWallets (UserId) ON DELETE CASCADE,
        -- No ON DELETE: an item somebody owns cannot be deleted from the shop.
        CONSTRAINT FK_LearnerItems_ShopItems FOREIGN KEY (ShopItemId)
            REFERENCES Gamification.ShopItems (Id),
        CONSTRAINT CK_LearnerItems_Quantity CHECK (Quantity > 0)
    );
END
GO


/* ============================================================================
   LearnerBoosts — a running XP multiplier. A window in time, not a possession,
   which is why it is not just a row in LearnerItems.
   ========================================================================== */

IF OBJECT_ID(N'Gamification.LearnerBoosts') IS NULL
BEGIN
    CREATE TABLE Gamification.LearnerBoosts
    (
        Id          BIGINT IDENTITY(1,1)    NOT NULL,
        UserId      UNIQUEIDENTIFIER        NOT NULL,
        ShopItemId  INT                     NOT NULL,
        Multiplier  TINYINT                 NOT NULL,
        StartedAt   DATETIME2(3)            NOT NULL,
        ExpiresAt   DATETIME2(3)            NOT NULL,

        CONSTRAINT PK_LearnerBoosts PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT FK_LearnerBoosts_LearnerWallets FOREIGN KEY (UserId)
            REFERENCES Gamification.LearnerWallets (UserId) ON DELETE CASCADE,
        CONSTRAINT FK_LearnerBoosts_ShopItems FOREIGN KEY (ShopItemId)
            REFERENCES Gamification.ShopItems (Id),
        CONSTRAINT CK_LearnerBoosts_Multiplier CHECK (Multiplier > 1),
        CONSTRAINT CK_LearnerBoosts_Window CHECK (ExpiresAt > StartedAt)
    );

    -- Serves "is a boost running right now?", the only question asked of this
    -- table on a hot path.
    CREATE INDEX IX_LearnerBoosts_UserId_ExpiresAt
        ON Gamification.LearnerBoosts (UserId, ExpiresAt);
END
GO


/* ============================================================================
   Seed — the starting catalogue. Matched on Code, so re-running does not
   duplicate anything and does not overwrite an admin's edits to price or text.
   ========================================================================== */

IF NOT EXISTS (SELECT 1 FROM Gamification.ShopItems WHERE Code = N'streak_freeze')
    INSERT INTO Gamification.ShopItems
        (Code, Kind, NameAr, NameEn, DescriptionAr, DescriptionEn, PriceSparks, MaxOwned, SortOrder)
    VALUES
        (N'streak_freeze', N'StreakFreeze',
         N'تجميد السلسلة', N'Streak Freeze',
         N'بيحمي سلسلتك لو نسيت تدخل يوم. بيتستخدم لوحده أول ما ترجع.',
         N'Protects your streak if you miss a day. It is used automatically when you come back.',
         200, 2, 1);
GO

IF NOT EXISTS (SELECT 1 FROM Gamification.ShopItems WHERE Code = N'double_xp_15')
    INSERT INTO Gamification.ShopItems
        (Code, Kind, NameAr, NameEn, DescriptionAr, DescriptionEn, PriceSparks, BoostMultiplier, BoostMinutes, SortOrder)
    VALUES
        (N'double_xp_15', N'Boost',
         N'نقاط خبرة مضاعفة', N'Double XP',
         N'نقاط خبرة مضاعفة لمدة ١٥ دقيقة. بيبدأ أول ما تشتريه.',
         N'Double XP for 15 minutes. It starts the moment you buy it.',
         120, 2, 15, 2);
GO

IF NOT EXISTS (SELECT 1 FROM Gamification.ShopItems WHERE Code = N'avatar_robot_helmet')
    INSERT INTO Gamification.ShopItems
        (Code, Kind, NameAr, NameEn, DescriptionAr, DescriptionEn, PriceSparks, MaxOwned, SortOrder)
    VALUES
        (N'avatar_robot_helmet', N'Avatar',
         N'خوذة الروبوت', N'Robot Helmet',
         N'خوذة روبوت لشخصيتك في اللعبة.',
         N'A robot helmet for your character.',
         150, 1, 3);
GO

IF NOT EXISTS (SELECT 1 FROM Gamification.ShopItems WHERE Code = N'avatar_astronaut_suit')
    INSERT INTO Gamification.ShopItems
        (Code, Kind, NameAr, NameEn, DescriptionAr, DescriptionEn, PriceSparks, MaxOwned, SortOrder)
    VALUES
        (N'avatar_astronaut_suit', N'Avatar',
         N'بدلة رائد الفضاء', N'Astronaut Suit',
         N'بدلة رائد فضاء لشخصيتك في اللعبة.',
         N'An astronaut suit for your character.',
         300, 1, 4);
GO

PRINT N'004: the Gamification schema and its starting shop catalogue are in place.';
GO
