CREATE TABLE [news].[Event] (
    [Level]   VARCHAR (1)        NOT NULL,
    [Country] VARCHAR (3)        NOT NULL,
    [Name]    VARCHAR (128)      NOT NULL,
    [Time]    DATETIMEOFFSET (7) NOT NULL,
    CONSTRAINT [PK_Event] PRIMARY KEY CLUSTERED ([Time] ASC, [Country] ASC, [Name] ASC) WITH (IGNORE_DUP_KEY = ON),
    CONSTRAINT [FK_Event_EventLevel] FOREIGN KEY ([Level]) REFERENCES [news].[EventLevel] ([Id])
);



