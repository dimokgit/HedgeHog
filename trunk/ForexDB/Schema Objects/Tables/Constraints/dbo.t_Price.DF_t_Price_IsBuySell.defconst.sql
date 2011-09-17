ALTER TABLE [dbo].[t_Price]
    ADD CONSTRAINT [DF_t_Price_IsBuySell] DEFAULT ((0)) FOR [IsBuySell];

