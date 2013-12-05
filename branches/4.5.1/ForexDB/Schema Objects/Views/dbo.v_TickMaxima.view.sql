CREATE VIEW v_TickMaxima AS
SELECT Price,AVG(Price1)PriceAvg,AVG(Volts)VoltsAvg,COUNT(*) Count
FROM v_TickMaxima_10
GROUP BY Price
