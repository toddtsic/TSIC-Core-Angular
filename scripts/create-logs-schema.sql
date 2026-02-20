-- Create logs schema and AppLog table for Serilog structured logging
-- Run against TSICV5 database

IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'logs')
    EXEC('CREATE SCHEMA logs');
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'logs' AND t.name = 'AppLog')
BEGIN
    CREATE TABLE logs.AppLog (
        Id          bigint IDENTITY(1,1) NOT NULL PRIMARY KEY CLUSTERED,
        TimeStamp   datetimeoffset(7)    NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        Level       nvarchar(16)         NOT NULL,
        Message     nvarchar(max)        NULL,
        Exception   nvarchar(max)        NULL,
        Properties  nvarchar(max)        NULL,   -- JSON
        SourceContext nvarchar(512)       NULL,   -- logger class name
        RequestPath nvarchar(512)        NULL,
        StatusCode  int                  NULL,
        Elapsed     float                NULL     -- ms
    );

    -- Index for common queries (time range + level filtering)
    CREATE NONCLUSTERED INDEX IX_AppLog_TimeStamp_Level
        ON logs.AppLog (TimeStamp DESC, Level)
        INCLUDE (Message, SourceContext, RequestPath, StatusCode);
END
GO
