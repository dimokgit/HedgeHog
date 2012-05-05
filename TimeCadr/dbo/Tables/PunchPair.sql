CREATE TABLE [dbo].[PunchPair] (
    [Start]        DATETIMEOFFSET (7) NOT NULL,
    [Stop]         DATETIMEOFFSET (7) NOT NULL,
    [TotalMinutes] AS                 (isnull(datediff(minute,[Start],[Stop]),(0))) PERSISTED NOT NULL,
    CONSTRAINT [PK_PunchPair_Start] PRIMARY KEY CLUSTERED ([Start] ASC),
    CONSTRAINT [FK_PunchPair_Punch] FOREIGN KEY ([Start]) REFERENCES [dbo].[Punch] ([Time]) ON DELETE NO ACTION ON UPDATE NO ACTION,
    CONSTRAINT [FK_PunchPair_Punch1] FOREIGN KEY ([Stop]) REFERENCES [dbo].[Punch] ([Time]) ON DELETE NO ACTION ON UPDATE NO ACTION
);


GO
ALTER TABLE [dbo].[PunchPair] NOCHECK CONSTRAINT [FK_PunchPair_Punch];


GO
ALTER TABLE [dbo].[PunchPair] NOCHECK CONSTRAINT [FK_PunchPair_Punch1];




GO
ALTER TABLE [dbo].[PunchPair] NOCHECK CONSTRAINT [FK_PunchPair_Punch];


GO
ALTER TABLE [dbo].[PunchPair] NOCHECK CONSTRAINT [FK_PunchPair_Punch1];




GO
ALTER TABLE [dbo].[PunchPair] NOCHECK CONSTRAINT [FK_PunchPair_Punch];


GO
ALTER TABLE [dbo].[PunchPair] NOCHECK CONSTRAINT [FK_PunchPair_Punch1];




GO
ALTER TABLE [dbo].[PunchPair] NOCHECK CONSTRAINT [FK_PunchPair_Punch];


GO
ALTER TABLE [dbo].[PunchPair] NOCHECK CONSTRAINT [FK_PunchPair_Punch1];




GO
ALTER TABLE [dbo].[PunchPair] NOCHECK CONSTRAINT [FK_PunchPair_Punch];


GO
ALTER TABLE [dbo].[PunchPair] NOCHECK CONSTRAINT [FK_PunchPair_Punch1];




GO
ALTER TABLE [dbo].[PunchPair] NOCHECK CONSTRAINT [FK_PunchPair_Punch];


GO
ALTER TABLE [dbo].[PunchPair] NOCHECK CONSTRAINT [FK_PunchPair_Punch1];




GO

GO
CREATE TRIGGER [dbo].[PunchPair#Delete] 
   ON  [dbo].[PunchPair] 
   AFTER INSERT,UPDATE,DELETE
AS 
BEGIN --1
IF @@ROWCOUNT = 0 RETURN
SET NOCOUNT ON;

-- Auto Meal Break
--PRINT 'Here'
DECLARE @MWDM int = config.MealBreakWorkMinimum()
DECLARE @MBDS int = config.MealBreakDeductionStart()
DECLARE @AMB int = config.AutoMealBreak()
--SELECT 'D',* FROM deleted
SELECT Row = ROW_NUMBER()OVER(ORDER BY(SELECT Start)),* 
INTO #New
FROM inserted i
WHERE DATEDIFF(MI,i.Start,i.Stop) >= @MWDM

--SELECT * FROM #New

DECLARE @Row int = (SELECT MIN(Row) FROM #New)
WHILE @Row > 0 BEGIN

INSERT INTO Punch(Time,DirectionId,TypeId,InputMethodId)
SELECT *,const.PunchInputMethodCalculated() InputMethodId
FROM
(
SELECT DATEADD(MI,@MBDS, N.Start)Start,const.PunchDirectionOut()DirectionId,const.PuncTypeLunch() TypeId FROM #New N WHERE Row = @Row
UNION ALL
SELECT DATEADD(MI,@MBDS + @AMB, N.Start),const.PunchDirectionIn(),const.PuncTypeLunch() FROM #New N WHERE Row = @Row
)T

SELECT @Row = Row FROM #New WHERE Row > @Row
IF @@ROWCOUNT = 0 BREAK  
END

DELETE FROM WS
FROM WorkShift WS
INNER JOIN deleted d ON d.Stop = WS.Start 

END
GO
CREATE UNIQUE NONCLUSTERED INDEX [IX_PunchPair_Stop]
    ON [dbo].[PunchPair]([Stop] ASC);

