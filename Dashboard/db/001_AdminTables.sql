/* =====================================================================
   VVI Admin Dashboard — Phase 1 schema
   Target database: VendorAPI_FK
   Safe to run: creates two NEW tables and seeds the first admin user.
   Does NOT touch any existing VVI table.
   Review before running (per working model: approve schema changes first).
   ===================================================================== */

/* ---------- 1. Admin login accounts ---------- */
IF OBJECT_ID('dbo.AdminUsers', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdminUsers
    (
        UserID        INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdminUsers PRIMARY KEY,
        Email         VARCHAR(255)  NOT NULL,
        Name          NVARCHAR(200) NULL,
        PasswordHash  VARCHAR(255)  NOT NULL,
        Role          VARCHAR(20)   NOT NULL CONSTRAINT DF_AdminUsers_Role     DEFAULT ('viewer'),
        IsActive      BIT           NOT NULL CONSTRAINT DF_AdminUsers_IsActive DEFAULT (1),
        LastLoginUtc  DATETIME2     NULL,
        CreatedUtc    DATETIME2     NOT NULL CONSTRAINT DF_AdminUsers_Created  DEFAULT (SYSUTCDATETIME()),
        CreatedBy     VARCHAR(255)  NULL,
        CONSTRAINT UQ_AdminUsers_Email UNIQUE (Email)
    );
END
GO

/* ---------- 2. 2FA challenge codes (hashed, never plain) ---------- */
IF OBJECT_ID('dbo.AdminTwoFactorCodes', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AdminTwoFactorCodes
    (
        CodeID      INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_AdminTwoFactorCodes PRIMARY KEY,
        UserID      INT           NOT NULL,
        CodeHash    VARCHAR(255)  NOT NULL,
        ExpiresUtc  DATETIME2     NOT NULL,
        Attempts    INT           NOT NULL CONSTRAINT DF_Admin2FA_Attempts DEFAULT (0),
        UsedUtc     DATETIME2     NULL,
        CreatedUtc  DATETIME2     NOT NULL CONSTRAINT DF_Admin2FA_Created  DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_Admin2FA_User FOREIGN KEY (UserID)
            REFERENCES dbo.AdminUsers (UserID) ON DELETE CASCADE
    );

    CREATE INDEX IX_Admin2FA_User ON dbo.AdminTwoFactorCodes (UserID, UsedUtc);
END
GO

/* ---------- 3. Seed the first admin ----------
   Temp password: ChangeMe!2026  (BCrypt, work factor 12)
   CHANGE THIS on first login once the login page lands.               */
IF NOT EXISTS (SELECT 1 FROM dbo.AdminUsers WHERE Email = 'glen@fullnet247.com')
BEGIN
    INSERT INTO dbo.AdminUsers (Email, Name, PasswordHash, Role, IsActive, CreatedBy)
    VALUES ('glen@fullnet247.com', 'Glen Thompson',
            '$2b$12$0wMnfRJQkvQrgy0aMqIqw.zyxV7Zq.guxLc.xke.rR9sFqiFjZu0.',
            'admin', 1, 'seed');
END
GO

/* =====================================================================
   PHASE C — HELD PENDING DECISION (do NOT run yet)
   ---------------------------------------------------------------------
   The plan proposed adding CustomerID / CustomerName / TechContact /
   IsActive to dbo.ClientProfiles to make it a "customer registry".

   Live schema check says that no longer fits:
     * dbo.ClientProfiles is the framework's SHIPPER-CODE routing table
       (ShipperCode + VendorName + EnabledEvents + ConfigJson) and it
       ALREADY has IsActive. It has no customer name/id.
     * Customer identity already lives on dbo.VVIProfiles
       (CustomerID INT + Customer VARCHAR(255)), one pair per profile row.

   So the Customer Registry should read from VVIProfiles, not ClientProfiles.
   Pick one before any ALTER runs — see README "Decision needed".
   ===================================================================== */
