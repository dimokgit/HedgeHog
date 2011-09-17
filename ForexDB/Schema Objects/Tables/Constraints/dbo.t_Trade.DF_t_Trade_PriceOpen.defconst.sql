ALTER TABLE [dbo].[t_Trade]
    ADD CONSTRAINT [DF_t_Trade_PriceOpen] DEFAULT ((0)) FOR [PriceOpen];

