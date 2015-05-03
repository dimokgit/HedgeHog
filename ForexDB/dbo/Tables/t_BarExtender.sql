CREATE TABLE [dbo].[t_BarExtender] (
    [ID]    INT          IDENTITY (1, 1) NOT NULL,
    [BarID] INT          NOT NULL,
    [Key]   VARCHAR (64) NOT NULL,
    [Value] FLOAT (53)   NOT NULL,
    CONSTRAINT [PK_t_BarExtender] PRIMARY KEY NONCLUSTERED ([ID] ASC),
    CONSTRAINT [FK_t_BarExtender_t_Bar] FOREIGN KEY ([BarID]) REFERENCES [dbo].[t_Bar] ([ID]) ON DELETE CASCADE
);


GO
CREATE UNIQUE CLUSTERED INDEX [IX_t_BarExtender]
    ON [dbo].[t_BarExtender]([BarID] ASC, [Key] ASC);

