-- Synthetic Kuwait CRM data for the local LeadManagement development database only.
-- Emails use the reserved .invalid domain; +965 7xxxxxxx is outside the valid
-- mobile prefixes in Kuwait's national numbering plan. Do not use for outreach.
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;

IF DB_NAME() <> N'LeadManagement'
    THROW 51000, 'This demo seed only runs against the LeadManagement database.', 1;

IF NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u
               JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
               JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
               WHERE r.Name = N'Admin')
    THROW 51001, 'Seed the admin account before loading demo data.', 1;

IF EXISTS (SELECT 1 FROM dbo.Leads WHERE Email NOT LIKE N'%@demo.invalid' OR Email IS NULL)
    THROW 51002, 'The database already has non-demo leads; refusing to mix sample data.', 1;

IF EXISTS (SELECT 1 FROM dbo.Leads WHERE Email = N'lead01@demo.invalid')
BEGIN
    PRINT 'Kuwait demo data is already present; no changes made.';
    RETURN;
END;

BEGIN TRANSACTION;

DECLARE @Now datetime2(0) = SYSUTCDATETIME();
DECLARE @AdminId bigint = (
    SELECT TOP (1) u.Id FROM dbo.AspNetUsers u
    JOIN dbo.AspNetUserRoles ur ON ur.UserId = u.Id
    JOIN dbo.AspNetRoles r ON r.Id = ur.RoleId
    WHERE r.Name = N'Admin' ORDER BY u.Id
);
DECLARE @ManagerRoleId bigint = (SELECT Id FROM dbo.AspNetRoles WHERE Name = N'Manager');
DECLARE @EmployeeRoleId bigint = (SELECT Id FROM dbo.AspNetRoles WHERE Name = N'Employee');

DECLARE @People table (FullName nvarchar(150), Email nvarchar(256), RoleName nvarchar(20));
INSERT INTO @People VALUES
    (N'Mariam Al-Haddad', N'mariam@team.demo.invalid', N'Manager'),
    (N'Yousef Al-Mutairi', N'yousef@team.demo.invalid', N'Employee'),
    (N'Sara Al-Ajmi', N'sara@team.demo.invalid', N'Employee'),
    (N'Omar Rahman', N'omar@team.demo.invalid', N'Employee'),
    (N'Anika Menon', N'anika@team.demo.invalid', N'Employee');

INSERT INTO dbo.AspNetUsers
    (FullName, IsActive, ManagerId, UserName, NormalizedUserName,
     Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp,
     ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled,
     LockoutEnabled, AccessFailedCount)
SELECT p.FullName, 1, NULL, p.Email, UPPER(p.Email),
       p.Email, UPPER(p.Email), 1, NULL, CONVERT(nvarchar(36), NEWID()),
       CONVERT(nvarchar(36), NEWID()), NULL, 0, 0, 1, 0
FROM @People p
WHERE NOT EXISTS (SELECT 1 FROM dbo.AspNetUsers u WHERE u.Email = p.Email);

DECLARE @ManagerId bigint = (SELECT Id FROM dbo.AspNetUsers WHERE Email = N'mariam@team.demo.invalid');
UPDATE dbo.AspNetUsers SET ManagerId = @ManagerId
WHERE Email IN (N'yousef@team.demo.invalid', N'sara@team.demo.invalid',
                N'omar@team.demo.invalid', N'anika@team.demo.invalid');

INSERT INTO dbo.AspNetUserRoles (UserId, RoleId)
SELECT u.Id, CASE WHEN p.RoleName = N'Manager' THEN @ManagerRoleId ELSE @EmployeeRoleId END
FROM @People p JOIN dbo.AspNetUsers u ON u.Email = p.Email
WHERE NOT EXISTS (SELECT 1 FROM dbo.AspNetUserRoles ur WHERE ur.UserId = u.Id);

-- These demo staff accounts have no password hash and cannot sign in until an
-- admin deliberately sets a password for them.
DECLARE @Leads table (
    DemoNo int, Name nvarchar(150), Phone nvarchar(30), Company nvarchar(200),
    Area nvarchar(500), Referrer nvarchar(150), Amount decimal(18,3),
    Requirement nvarchar(2000), SourceId bigint, StatusId bigint,
    OwnerEmail nvarchar(256), DaysAgo int
);
INSERT INTO @Leads VALUES
 (1,N'Fahad Al-Rashid',N'+965 7000 0101',N'Bayt Al Noor Trading',N'Salmiya, Block 10, Salem Al Mubarak Street',N'Abdullah Al-Saleh',7850.000,N'Retail showroom fit-out with lighting and display joinery.',4,4,N'sara@team.demo.invalid',32),
 (2,N'Layla Al-Dosari',N'+965 7000 0102',N'Marasi Hospitality',N'Sharq, Block 2, Gulf Road',N'Website enquiry',18600.000,N'Cafe interior renovation and kitchen service counters.',2,3,N'yousef@team.demo.invalid',21),
 (3,N'Khaled Al-Enezi',N'+965 7000 0103',N'Al Waha Logistics',N'Shuwaikh Industrial Area, Block 1',N'Hamad Al-Otaibi',12450.000,N'Warehouse office partitions and reception area.',1,2,N'omar@team.demo.invalid',18),
 (4,N'Noor Al-Faraj',N'+965 7000 0104',N'Safa Medical Services',N'Jabriya, Block 3, Street 14',N'Instagram campaign',9650.000,N'Clinic reception, waiting area and signage refresh.',3,4,N'anika@team.demo.invalid',42),
 (5,N'Abdullah Al-Shammari',N'+965 7000 0105',N'Dar Al Qamar Properties',N'Sabah Al-Salem, Block 8',N'Mariam Al-Haddad',34750.000,N'Apartment common-area refurbishment across two buildings.',4,5,N'mariam@team.demo.invalid',88),
 (6,N'Hessa Al-Mutairi',N'+965 7000 0106',N'Palm Avenue Boutique',N'Kuwait City, Mubarakiya district',N'Sara Al-Ajmi',4250.000,N'Boutique shelving, fitting rooms and new storefront.',1,3,N'sara@team.demo.invalid',14),
 (7,N'Rashid Al-Hajri',N'+965 7000 0107',N'Gulf Pearl Kitchens',N'Fahaheel, Block 7',N'Website enquiry',15200.000,N'Kitchen display centre with three sample layouts.',2,7,N'yousef@team.demo.invalid',64),
 (8,N'Salma Al-Awadi',N'+965 7000 0108',N'Blue Dhow Consulting',N'Hawally, Block 4, Tunis Street',N'Omar Rahman',6900.000,N'Open-plan office acoustic partitions and meeting room.',1,2,N'omar@team.demo.invalid',11),
 (9,N'Jassim Al-Kandari',N'+965 7000 0109',N'Kayan Fitness Studio',N'Mahboula, Block 2',N'Facebook enquiry',11875.000,N'Fitness studio flooring, mirrors and locker area.',3,1,N'anika@team.demo.invalid',3),
 (10,N'Dana Al-Sabah',N'+965 7000 0110',N'Pearl Gate Events',N'Bneid Al-Gar, Block 1',N'Abdullah Al-Saleh',5750.000,N'Event office reception and modular storage.',4,6,N'sara@team.demo.invalid',53),
 (11,N'Ahmed Al-Ali',N'+965 7000 0111',N'Al Sayer Garden Supplies',N'Farwaniya, Block 5',N'Website enquiry',8950.000,N'Showroom display units and exterior signage.',2,5,N'yousef@team.demo.invalid',71),
 (12,N'Reem Al-Qahtani',N'+965 7000 0112',N'Nakhla Design House',N'Mishref, Block 6',N'Layla Al-Dosari',13400.000,N'Villa majlis interior concept and material selection.',1,3,N'mariam@team.demo.invalid',25),
 (13,N'Tariq Al-Harbi',N'+965 7000 0113',N'Kuwait Bay Workspace',N'Salhiya, Block 3',N'WhatsApp enquiry',22300.000,N'Flexible workspace refurbishment across two floors.',4,4,N'omar@team.demo.invalid',38),
 (14,N'Amal Al-Refai',N'+965 7000 0114',N'Cedar Corner Bakery',N'Rumaithiya, Block 9',N'Facebook enquiry',3150.000,N'Bakery counter and customer seating upgrade.',3,9,N'anika@team.demo.invalid',29),
 (15,N'Bader Al-Otaibi',N'+965 7000 0115',N'Dar Al Watan Services',N'Jahra, Block 1',N'Website enquiry',48750.000,N'New branch office fit-out including HVAC and joinery.',2,2,N'yousef@team.demo.invalid',17),
 (16,N'Priya Nair',N'+965 7000 0116',N'Marina View Optics',N'Salmiya, Marina area',N'Anika Menon',5250.000,N'Optical store display cabinetry and lighting.',1,8,N'anika@team.demo.invalid',96),
 (17,N'Mohammed Al-Fares',N'+965 7000 0117',N'Afaq Learning Centre',N'Qortuba, Block 4',N'Website enquiry',10800.000,N'Training rooms, AV wall and reception refurbishment.',2,7,N'omar@team.demo.invalid',47),
 (18,N'Fatima Al-Marzouq',N'+965 7000 0118',N'Rosewater Spa',N'Sabah Al-Salem, Block 2',N'Sara Al-Ajmi',14250.000,N'Spa treatment rooms and wash area upgrade.',4,5,N'sara@team.demo.invalid',77),
 (19,N'Adel Al-Naqi',N'+965 7000 0119',N'Harbor Line Transport',N'Shuwaikh Industrial Area, Block 3',N'Website enquiry',6400.000,N'Dispatch office renovation and storage cabinets.',2,6,N'yousef@team.demo.invalid',59),
 (20,N'Maya Al-Ghanim',N'+965 7000 0120',N'Olive Branch Florists',N'Fintas, Block 4',N'WhatsApp enquiry',2950.000,N'Cold storage display and shopfront refresh.',4,1,N'anika@team.demo.invalid',2),
 (21,N'Yaqoub Al-Roumi',N'+965 7000 0121',N'Sandstone Advisory',N'Kuwait City, Sharq',N'Hamad Al-Otaibi',7700.000,N'Executive meeting room and acoustic treatment.',1,8,N'mariam@team.demo.invalid',104),
 (22,N'Shazia Khan',N'+965 7000 0122',N'Desert Bloom Nursery',N'Abu Halifa, Block 1',N'Facebook enquiry',4200.000,N'Nursery reception and child-safe storage.',3,2,N'omar@team.demo.invalid',9),
 (23,N'Yousef Al-Ajmi',N'+965 7000 0123',N'Coastal Med Supplies',N'Ahmadi, Block 6',N'Website enquiry',16800.000,N'Medical supplies showroom and inventory office.',2,3,N'yousef@team.demo.invalid',23),
 (24,N'Meera Al-Haddad',N'+965 7000 0124',N'Riwaq Homeware',N'Hawally, Block 8',N'WhatsApp enquiry',9800.000,N'Homeware display walls, cashier counter and lighting.',4,1,N'sara@team.demo.invalid',1);

INSERT INTO dbo.Leads
    (Name, Phone, Email, CompanyName, Address, LeadGivenBy, ProjectAmount,
     Requirement, SourceId, StatusId, AssignedToId, CreatedById, CreatedAtUtc, UpdatedAtUtc)
SELECT d.Name, d.Phone, CONCAT(N'lead', RIGHT(CONCAT(N'00', d.DemoNo), 2), N'@demo.invalid'),
       d.Company, d.Area, d.Referrer, d.Amount, d.Requirement,
       d.SourceId, d.StatusId, u.Id, @AdminId,
       DATEADD(DAY, -d.DaysAgo, @Now), DATEADD(DAY, -IIF(d.DaysAgo > 3, 2, 0), @Now)
FROM @Leads d JOIN dbo.AspNetUsers u ON u.Email = d.OwnerEmail;

UPDATE l SET CustomerCode = CONCAT(N'CUS-', YEAR(l.CreatedAtUtc), N'-',
                                   RIGHT(CONCAT(N'000000', l.Id), 6))
FROM dbo.Leads l WHERE l.Email LIKE N'lead%@demo.invalid';

INSERT INTO dbo.LeadAssignments (LeadId, PreviousUserId, AssignedToId, AssignedById, AssignedAtUtc)
SELECT l.Id, NULL, l.AssignedToId, @AdminId, DATEADD(HOUR, 3, l.CreatedAtUtc)
FROM dbo.Leads l WHERE l.Email LIKE N'lead%@demo.invalid';

INSERT INTO dbo.LeadStatusChanges (LeadId, OldStatusId, NewStatusId, ChangedById, ChangedAtUtc)
SELECT l.Id, 1, l.StatusId, @AdminId, DATEADD(DAY, 2, l.CreatedAtUtc)
FROM dbo.Leads l WHERE l.Email LIKE N'lead%@demo.invalid' AND l.StatusId <> 1;

INSERT INTO dbo.LeadFollowUps
    (LeadId, AssignedToId, Notes, DueAtUtc, CompletedAtUtc, CreatedById, CreatedAtUtc)
SELECT l.Id, l.AssignedToId,
       CASE l.StatusId
           WHEN 4 THEN N'Site visit completed; detailed quotation shared.'
           WHEN 5 THEN N'Purchase order confirmed and handover discussed.'
           WHEN 6 THEN N'Client chose a different supplier after review.'
           WHEN 8 THEN N'Final handover completed and feedback requested.'
           WHEN 9 THEN N'Client deferred the project for this year.'
           ELSE N'Initial consultation completed; scope and budget discussed.'
       END,
       DATEADD(DAY, -IIF(d.DaysAgo > 10, 7, 1), @Now),
       DATEADD(DAY, -IIF(d.DaysAgo > 10, 7, 1), @Now),
       @AdminId, DATEADD(DAY, -IIF(d.DaysAgo > 10, 10, 2), @Now)
FROM dbo.Leads l JOIN @Leads d ON l.Phone = d.Phone
WHERE d.DaysAgo > 7;

INSERT INTO dbo.LeadFollowUps
    (LeadId, AssignedToId, Notes, DueAtUtc, CompletedAtUtc, CreatedById, CreatedAtUtc)
SELECT l.Id, l.AssignedToId,
       CASE l.StatusId
           WHEN 1 THEN N'Call to confirm the project scope and preferred site visit.'
           WHEN 2 THEN N'Request drawings and confirm the budget range.'
           WHEN 3 THEN N'Schedule site survey and finalise measurements.'
           WHEN 4 THEN N'Follow up on quotation approval and revised scope.'
           WHEN 7 THEN N'Check execution milestones and next payment stage.'
       END,
       CASE d.DemoNo % 4
           WHEN 0 THEN DATEADD(DAY, -2, @Now)
           WHEN 1 THEN DATEADD(DAY, 2, @Now)
           WHEN 2 THEN DATEADD(DAY, 5, @Now)
           ELSE DATEADD(HOUR, 8, @Now)
       END,
       NULL, @AdminId, DATEADD(DAY, -IIF(d.DaysAgo > 4, 4, d.DaysAgo), @Now)
FROM dbo.Leads l JOIN @Leads d ON l.Phone = d.Phone
WHERE l.StatusId IN (1,2,3,4,7);

UPDATE l SET
    LastFollowUpDate = (SELECT MAX(f.CompletedAtUtc) FROM dbo.LeadFollowUps f
                        WHERE f.LeadId = l.Id AND f.CompletedAtUtc IS NOT NULL),
    NextFollowUpDate = (SELECT MIN(f.DueAtUtc) FROM dbo.LeadFollowUps f
                        WHERE f.LeadId = l.Id AND f.CompletedAtUtc IS NULL)
FROM dbo.Leads l WHERE l.Email LIKE N'lead%@demo.invalid';

INSERT INTO dbo.AuditLogs (UserId, Action, TableName, RecordId, ChangedFields, CreatedAtUtc)
SELECT @AdminId, N'Create', N'Leads', l.Id, N'Demo seed', l.CreatedAtUtc
FROM dbo.Leads l WHERE l.Email LIKE N'lead%@demo.invalid';

COMMIT TRANSACTION;
PRINT 'Inserted 5 demo staff, 24 Kuwait-style leads and related follow-ups.';
