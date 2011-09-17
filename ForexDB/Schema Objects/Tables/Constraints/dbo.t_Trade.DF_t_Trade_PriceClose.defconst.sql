ALTER TABLE [dbo].[t_Trade]
    ADD CONSTRAINT [DF_t_Trade_PriceClose] DEFAULT ((0)) FOR [PriceClose];

