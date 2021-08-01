CREATE AGGREGATE [dbo].[Linear](@y FLOAT (53) NULL)
    RETURNS FLOAT (53)
    EXTERNAL NAME [SQL_DateFuncs].[Linear];

