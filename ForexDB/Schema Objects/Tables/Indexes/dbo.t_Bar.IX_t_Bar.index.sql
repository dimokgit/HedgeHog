CREATE UNIQUE CLUSTERED INDEX [IX_t_Bar]
    ON [dbo].[t_Bar]([Pair] ASC, [Period] ASC, [StartDate] ASC, [Row] ASC) WITH (IGNORE_DUP_KEY = ON);



