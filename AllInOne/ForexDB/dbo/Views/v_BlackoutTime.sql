
CREATE VIEW [dbo].[v_BlackoutTime] AS
SELECT *
, DATEADD(HH,-12,Time)TimeStart
, Time TimeStop
, DATEADD(HH,-12,TimeLocal)TimeStartLocal
, TimeLocal TimeStopLocal
FROM t_Report
WHERE EventType = 'R'

UNION ALL

SELECT *
, DATEADD(HH,-12,Time)TimeStart
, DATEADD(HH,12,Time)TimeStop
, DATEADD(HH,-12,TimeLocal)TimeStartLocal
, DATEADD(HH,12,TimeLocal)TimeStopLocal
FROM t_Report
WHERE EventType = 'H'

