-- Apply only to an existing CRM database with its original schema.
-- New installations should create the EF Core initial migration instead.
SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF COL_LENGTH('dbo.Leads', 'CustomerCode') IS NULL
    ALTER TABLE dbo.Leads ADD CustomerCode nvarchar(30) NULL;
IF COL_LENGTH('dbo.Leads', 'Address') IS NULL
    ALTER TABLE dbo.Leads ADD Address nvarchar(500) NULL;
IF COL_LENGTH('dbo.Leads', 'LeadGivenBy') IS NULL
    ALTER TABLE dbo.Leads ADD LeadGivenBy nvarchar(150) NULL;
IF COL_LENGTH('dbo.Leads', 'ProjectAmount') IS NULL
    ALTER TABLE dbo.Leads ADD ProjectAmount decimal(18, 3) NULL;
IF COL_LENGTH('dbo.Leads', 'LastFollowUpDate') IS NULL
    ALTER TABLE dbo.Leads ADD LastFollowUpDate datetime2 NULL;
IF COL_LENGTH('dbo.Leads', 'NextFollowUpDate') IS NULL
    ALTER TABLE dbo.Leads ADD NextFollowUpDate datetime2 NULL;
IF COL_LENGTH('dbo.Leads', 'ModifiedByUserId') IS NULL
    ALTER TABLE dbo.Leads ADD ModifiedByUserId bigint NULL;

UPDATE dbo.Leads
SET CustomerCode = CONCAT('CUS-', YEAR(CreatedAtUtc), '-',
    CASE WHEN LEN(CONVERT(varchar(20), Id)) >= 6 THEN CONVERT(varchar(20), Id)
         ELSE RIGHT('000000' + CONVERT(varchar(20), Id), 6) END)
WHERE CustomerCode IS NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Leads') AND name = 'IX_Leads_CustomerCode')
    CREATE UNIQUE INDEX IX_Leads_CustomerCode ON dbo.Leads(CustomerCode)
        WHERE CustomerCode IS NOT NULL;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Leads') AND name = 'IX_Leads_LeadGivenBy')
    CREATE INDEX IX_Leads_LeadGivenBy ON dbo.Leads(LeadGivenBy);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Leads') AND name = 'IX_Leads_ModifiedByUserId')
    CREATE INDEX IX_Leads_ModifiedByUserId ON dbo.Leads(ModifiedByUserId);

IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = 'FK_Leads_AspNetUsers_ModifiedByUserId')
    ALTER TABLE dbo.Leads ADD CONSTRAINT FK_Leads_AspNetUsers_ModifiedByUserId
    FOREIGN KEY (ModifiedByUserId) REFERENCES dbo.AspNetUsers(Id);

UPDATE l SET
    LastFollowUpDate = (SELECT MAX(f.CompletedAtUtc)
        FROM dbo.LeadFollowUps f WHERE f.LeadId = l.Id),
    NextFollowUpDate = (SELECT MIN(f.DueAtUtc)
        FROM dbo.LeadFollowUps f WHERE f.LeadId = l.Id AND f.CompletedAtUtc IS NULL)
FROM dbo.Leads l
WHERE l.LastFollowUpDate IS NULL OR l.NextFollowUpDate IS NULL;

IF OBJECT_ID('dbo.AuditLogs', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLogs
    (
        Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_AuditLogs PRIMARY KEY,
        UserId bigint NOT NULL,
        Action nvarchar(50) NOT NULL,
        TableName nvarchar(60) NOT NULL,
        RecordId bigint NOT NULL,
        ChangedFields nvarchar(1000) NULL,
        CreatedAtUtc datetime2 NOT NULL
    );
END;

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.AuditLogs') AND name = 'IX_AuditLogs_TableName_RecordId')
    CREATE INDEX IX_AuditLogs_TableName_RecordId ON dbo.AuditLogs(TableName, RecordId);

SET IDENTITY_INSERT dbo.LeadStatuses ON;
INSERT INTO dbo.LeadStatuses (Id, Name, SortOrder)
SELECT v.Id, v.Name, v.SortOrder
FROM (VALUES (7, 'Active', 7), (8, 'Closed', 8), (9, 'Not Interested', 9)) v(Id, Name, SortOrder)
WHERE NOT EXISTS (SELECT 1 FROM dbo.LeadStatuses s WHERE s.Id = v.Id);
SET IDENTITY_INSERT dbo.LeadStatuses OFF;

COMMIT TRANSACTION;
