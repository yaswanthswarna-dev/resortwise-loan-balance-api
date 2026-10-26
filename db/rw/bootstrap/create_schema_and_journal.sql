-- Bootstrap for the rw schema. Run once by tools/ResortWise.RwMigrator, only when schema rw does
-- not exist, and never passed to DbUp. Creates schema rw and the DbUp journal table, nothing else.
-- Columns match the SQL Server journal of dbup-sqlserver 5.0.41.
CREATE SCHEMA rw AUTHORIZATION dbo;
GO

CREATE TABLE rw.SchemaVersions
(
    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SchemaVersions_Id PRIMARY KEY,
    ScriptName nvarchar(255) NOT NULL,
    Applied datetime NOT NULL
);
GO
