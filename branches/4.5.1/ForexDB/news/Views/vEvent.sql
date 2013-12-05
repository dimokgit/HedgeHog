CREATE VIEW news.vEvent
AS
SELECT        news.Event.Country, news.Event.Name, news.Event.Time, news.EventLevel.Name AS LevelName, news.EventLevel.[Level], news.EventLevel.Id AS LevelId
FROM            news.Event INNER JOIN
                         news.EventLevel ON news.Event.[Level] = news.EventLevel.Id