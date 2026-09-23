/*
    MRA Internal Policy Chatbot — Database Setup
    =============================================
    SQL Server (T-SQL) script. Safe to re-run: every statement is guarded
    with an existence check, so running this script multiple times against
    the same server will not error or duplicate data.

    This script creates schema and two seed accounts (one Admin, one
    Employee — see section 4 for credentials). It does NOT set up
    permissions beyond the default, or any business logic beyond basic
    CRUD — authentication logic itself lives in the application
    (Services/PasswordHasher.cs, Controllers/AccountController.cs).

    Usage (see README.md for full instructions):
        sqlcmd -S localhost -E -i database-setup.sql
*/

-- =========================================================
-- 1. Database
-- =========================================================
IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE name = N'MraPolicyChatbot')
BEGIN
    CREATE DATABASE MraPolicyChatbot;
END
GO

USE MraPolicyChatbot;
GO

-- =========================================================
-- 2. Tables
-- =========================================================

-- Users: application accounts. Roles are a plain text column ('Admin' /
-- 'Employee') by convention — no separate Roles table in this phase.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Users')
BEGIN
    CREATE TABLE dbo.Users
    (
        Id            INT IDENTITY(1,1) NOT NULL,
        Username      NVARCHAR(100)     NOT NULL,
        PasswordHash  NVARCHAR(255)     NOT NULL,
        Role          NVARCHAR(50)      NOT NULL,
        CreatedDate   DATETIME2         NOT NULL CONSTRAINT DF_Users_CreatedDate DEFAULT (GETDATE()),
        IsActive      BIT               NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT (1),
        CONSTRAINT PK_Users PRIMARY KEY (Id)
    );
END
GO

-- Policies: approved policy document metadata + extracted text content.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'Policies')
BEGIN
    CREATE TABLE dbo.Policies
    (
        Id            INT IDENTITY(1,1) NOT NULL,
        Title         NVARCHAR(255)     NOT NULL,
        Category      NVARCHAR(100)     NOT NULL,
        FilePath      NVARCHAR(500)     NULL,
        ContentText   NVARCHAR(MAX)     NULL,
        UploadedBy    INT               NULL,
        UploadDate    DATETIME2         NOT NULL CONSTRAINT DF_Policies_UploadDate DEFAULT (GETDATE()),
        Version       INT               NOT NULL CONSTRAINT DF_Policies_Version DEFAULT (1),
        IsActive      BIT               NOT NULL CONSTRAINT DF_Policies_IsActive DEFAULT (1),
        IsProcessed   BIT               NOT NULL CONSTRAINT DF_Policies_IsProcessed DEFAULT (0),
        ProcessingError NVARCHAR(MAX)   NULL,
        CONSTRAINT PK_Policies PRIMARY KEY (Id),
        CONSTRAINT FK_Policies_Users_UploadedBy FOREIGN KEY (UploadedBy) REFERENCES dbo.Users (Id)
    );
END
GO

-- Phase 5: add IsProcessed / ProcessingError to a Policies table that was
-- already created by an earlier run of this script (before Phase 5).
-- Fresh installs get these columns from the CREATE TABLE above instead.
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Policies') AND name = N'IsProcessed'
)
BEGIN
    ALTER TABLE dbo.Policies ADD IsProcessed BIT NOT NULL CONSTRAINT DF_Policies_IsProcessed DEFAULT (0);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.Policies') AND name = N'ProcessingError'
)
BEGIN
    ALTER TABLE dbo.Policies ADD ProcessingError NVARCHAR(MAX) NULL;
END
GO

-- PolicyChunks: policy text split into chunks for future retrieval/AI use.
-- Embedding is a placeholder NVARCHAR(MAX) column in this phase — no AI
-- integration exists yet, so no vector type or embedding values are used.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'PolicyChunks')
BEGIN
    CREATE TABLE dbo.PolicyChunks
    (
        Id            INT IDENTITY(1,1) NOT NULL,
        PolicyId      INT               NOT NULL,
        ChunkText     NVARCHAR(MAX)     NOT NULL,
        ChunkOrder    INT               NOT NULL,
        Embedding     NVARCHAR(MAX)     NULL,
        CreatedDate   DATETIME2         NOT NULL CONSTRAINT DF_PolicyChunks_CreatedDate DEFAULT (GETDATE()),
        CONSTRAINT PK_PolicyChunks PRIMARY KEY (Id),
        CONSTRAINT FK_PolicyChunks_Policies_PolicyId FOREIGN KEY (PolicyId) REFERENCES dbo.Policies (Id)
    );
END
GO

-- AuditLog: generic action log (who did what, when).
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'AuditLog')
BEGIN
    CREATE TABLE dbo.AuditLog
    (
        Id            INT IDENTITY(1,1) NOT NULL,
        UserId        INT               NULL,
        Action        NVARCHAR(100)     NOT NULL,
        EntityType    NVARCHAR(100)     NULL,
        EntityId      INT               NULL,
        Timestamp     DATETIME2         NOT NULL CONSTRAINT DF_AuditLog_Timestamp DEFAULT (GETDATE()),
        Details       NVARCHAR(MAX)     NULL,
        CONSTRAINT PK_AuditLog PRIMARY KEY (Id),
        CONSTRAINT FK_AuditLog_Users_UserId FOREIGN KEY (UserId) REFERENCES dbo.Users (Id)
    );
END
GO

-- AppSettings: simple key/value store backing the Admin "Settings" page
-- (AI connection details, the answer-matching threshold, the department
-- list) so an admin can change these at runtime instead of editing
-- appsettings.json/C# code and rebuilding. See Data/AppSettingsRepository.cs.
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = N'AppSettings')
BEGIN
    CREATE TABLE dbo.AppSettings
    (
        [Key]   NVARCHAR(100)  NOT NULL,
        [Value] NVARCHAR(MAX)  NULL,
        CONSTRAINT PK_AppSettings PRIMARY KEY ([Key])
    );
END
GO

-- =========================================================
-- 3. Indexes
-- =========================================================

-- Usernames must be unique and are looked up on every login attempt.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Users_Username' AND object_id = OBJECT_ID(N'dbo.Users'))
BEGIN
    CREATE UNIQUE INDEX UX_Users_Username ON dbo.Users (Username);
END
GO

-- Policies are filtered/browsed by category.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Policies_Category' AND object_id = OBJECT_ID(N'dbo.Policies'))
BEGIN
    CREATE INDEX IX_Policies_Category ON dbo.Policies (Category);
END
GO

-- Chunks are always looked up by their parent policy.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_PolicyChunks_PolicyId' AND object_id = OBJECT_ID(N'dbo.PolicyChunks'))
BEGIN
    CREATE INDEX IX_PolicyChunks_PolicyId ON dbo.PolicyChunks (PolicyId);
END
GO

-- =========================================================
-- 4. Seed data
-- =========================================================

-- Two test accounts, one per role, so both the Admin and Employee paths can
-- be exercised. PasswordHash values are real bcrypt hashes (cost factor 10,
-- matching Services/PasswordHasher.cs) for the plaintext passwords below —
-- these are throwaway local/dev credentials, not production secrets:
--   admin    / admin123
--   employee / employee123
IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Username = N'admin')
BEGIN
    INSERT INTO dbo.Users (Username, PasswordHash, Role, CreatedDate, IsActive)
    VALUES (N'admin', N'$2b$10$yS4y2rYuPeakhxzfG7ny6etU3e3Ms8QG2BbApgfVNqP9ZRb9CyUZC', N'Admin', GETDATE(), 1);
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.Users WHERE Username = N'employee')
BEGIN
    INSERT INTO dbo.Users (Username, PasswordHash, Role, CreatedDate, IsActive)
    VALUES (N'employee', N'$2b$10$dcZu9v4UH5JdHEFjDdPrS.DEK6ZbyiMhl8QLlBE9lmmemperHOSE2', N'Employee', GETDATE(), 1);
END
GO

-- Default AppSettings rows, matching the values that were previously
-- hardcoded/in appsettings.json, so behavior is unchanged until an admin
-- actually edits something on the Settings page. Each is only inserted if
-- missing, so re-running this script never overwrites a value an admin
-- has already changed.
IF NOT EXISTS (SELECT 1 FROM dbo.AppSettings WHERE [Key] = N'Ollama:BaseUrl')
BEGIN
    INSERT INTO dbo.AppSettings ([Key], [Value]) VALUES (N'Ollama:BaseUrl', N'http://localhost:11434');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.AppSettings WHERE [Key] = N'Ollama:EmbeddingModel')
BEGIN
    INSERT INTO dbo.AppSettings ([Key], [Value]) VALUES (N'Ollama:EmbeddingModel', N'nomic-embed-text');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.AppSettings WHERE [Key] = N'Ollama:ChatModel')
BEGIN
    INSERT INTO dbo.AppSettings ([Key], [Value]) VALUES (N'Ollama:ChatModel', N'llama3.2');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.AppSettings WHERE [Key] = N'Chat:SimilarityThreshold')
BEGIN
    -- 0.35 rather than the original 0.5: 0.5 was rejecting loosely/
    -- informally worded (but genuinely on-topic) questions before the AI
    -- ever saw them. Only affects fresh installs — on an existing
    -- database, change this from Admin > Settings instead, since this
    -- INSERT is skipped once the row already exists.
    INSERT INTO dbo.AppSettings ([Key], [Value]) VALUES (N'Chat:SimilarityThreshold', N'0.35');
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.AppSettings WHERE [Key] = N'Departments:List')
BEGIN
    INSERT INTO dbo.AppSettings ([Key], [Value]) VALUES (N'Departments:List', N'Customs Division
Domestic Taxes Division
Administration
Corporate Affairs
Debt Management
Enterprise Wide Risk Management
Finance
Human Resource and Organisation Development
Information & Communication Technology (ICT)
Internal Affairs
Internal Audit
Legal Services
Modernisation
Policy Planning & Research
Supply Chain Management
Tax Investigations');
END
GO

-- =========================================================
-- 5. Handy verification queries (Phase 5)
-- =========================================================
-- Not part of setup — kept here for convenience. See README.md for how to
-- run these.
--
-- Processing status per policy, with chunk counts:
--   SELECT p.Id, p.Title, p.IsProcessed, p.ProcessingError,
--          COUNT(c.Id) AS ChunkCount
--   FROM dbo.Policies p
--   LEFT JOIN dbo.PolicyChunks c ON c.PolicyId = p.Id
--   GROUP BY p.Id, p.Title, p.IsProcessed, p.ProcessingError
--   ORDER BY p.Id;
--
-- Chunks for a specific policy, in order:
--   SELECT ChunkOrder, LEFT(ChunkText, 100) AS ChunkPreview
--   FROM dbo.PolicyChunks
--   WHERE PolicyId = @PolicyId
--   ORDER BY ChunkOrder;
