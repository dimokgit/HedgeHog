CREATE TABLE [dbo].[t_BarExtender] (
    [ID]    INT          IDENTITY (1, 1) NOT NULL,
    [BarID] INT          NOT NULL,
    [Key]   VARCHAR (64) NOT NULL,
    [Value] FLOAT (53)   NOT NULL,
    CONSTRAINT [PK_t_BarExtender] PRIMARY KEY NONCLUSTERED ([ID] ASC)
);

