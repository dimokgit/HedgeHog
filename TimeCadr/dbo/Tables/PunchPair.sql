﻿CREATE TABLE [dbo].[PunchPair] (
    [Start] DATETIMEOFFSET (7) NOT NULL,
    [Stop]  DATETIMEOFFSET (7) NOT NULL,
    CONSTRAINT [PK_PunchPair] PRIMARY KEY CLUSTERED ([Start] ASC)
);

