ALTER TABLE [dbo].[t_Currency]
    ADD CONSTRAINT [DF_t_Currency_IsPrime] DEFAULT ((0)) FOR [IsPrime];

