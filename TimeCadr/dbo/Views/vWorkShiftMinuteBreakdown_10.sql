CREATE VIEW vWorkShiftMinuteBreakdown_10 AS
SELECT   
dbo.WorkShift.Start AS WorkShiftStart
, PP.Start AS PunchPairStart
, CASE startdate WHEN stopdate THEN startdate ELSE dbo.getdate(minutedatetime) END AS MinuteDate
, PP.MinuteDateTime,
ROW_NUMBER() OVER (PARTITION BY dbo.WorkShift.Start ORDER BY PP.Start,PP.MInute) WorkShiftMinute
FROM dbo.vPunchPairMinute AS PP
INNER JOIN dbo.WorkShift ON PP.Start BETWEEN dbo.WorkShift.Start AND dbo.WorkShift.Stop