﻿ALTER TABLE [dbo].[t_Currency]
    ADD CONSTRAINT [PK_t_Currency] PRIMARY KEY CLUSTERED ([Name] ASC) WITH (ALLOW_PAGE_LOCKS = ON, ALLOW_ROW_LOCKS = ON, PAD_INDEX = OFF, IGNORE_DUP_KEY = OFF, STATISTICS_NORECOMPUTE = OFF);

