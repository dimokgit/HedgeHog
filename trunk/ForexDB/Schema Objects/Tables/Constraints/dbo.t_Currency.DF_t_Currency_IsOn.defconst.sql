ALTER TABLE [dbo].[t_Currency]
    ADD CONSTRAINT [DF_t_Currency_IsOn] DEFAULT ((1)) FOR [IsOn];

