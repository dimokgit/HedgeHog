CREATE PROCEDURE [dbo].[s_SetBarRows]
  @Pair varchar(30)
, @Period int
AS
DECLARE @D datetimeoffset
SELECT @D = MIN(StartDate) FROM t_Bar WHERE Row IS NULL

DECLARE @DoAll bit
SELECT TOP 1 @DoAll = 0 FROM t_Bar WHERE Pair = @Pair AND Period = 1 AND @D < StartDate

IF @DoAll = 0 BEGIN
UPDATE t_bar SET Row = B1.Row
FROM
t_Bar B
INNER JOIN
(
SELECT ROW_NUMBER()OVER(PARTITION BY Pair,Period ORDER BY Pair,Period,StartDate) Row,ID
  FROM t_Bar B WITH (NOLOCK)
  WHERE B.Pair = @Pair AND Period = 1 --AND StartDate >= @StartDate
)B1 ON B.ID = B1.ID
RETURN
END
DECLARE @Row int
SELECT @D = MAX(StartDate) FROM t_Bar WHERE NOT Row IS NULL
SELECT @Row = MAX(Row) FROM t_Bar WHERE Pair = @Pair AND Period = 1 AND StartDate>@D

IF NOT @Row IS NULL
UPDATE t_bar SET Row = B1.Row + @Row
FROM
t_Bar B
INNER JOIN
(
SELECT ROW_NUMBER()OVER(PARTITION BY Pair,Period ORDER BY Pair,Period,StartDate) Row,ID
  FROM t_Bar B WITH (NOLOCK)
  WHERE B.Pair = @Pair AND Period = 1 --AND StartDate >= @StartDate
)B1 ON B.ID = B1.ID
WHERE B.StartDate > @D