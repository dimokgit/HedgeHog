ALTER TABLE [dbo].[t_Trade]
    ADD CONSTRAINT [DF_t_Trade_TimeStamp] DEFAULT (getdate()) FOR [TimeStamp];

