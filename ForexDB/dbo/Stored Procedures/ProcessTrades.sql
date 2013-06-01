CREATE PROCEDURE [dbo].[ProcessTrades]--  @SessionId- = 'E98D7FC3-A8A5-4B51-85CF-366362C57F50'
	@SessionId uniqueidentifier
AS 
DECLARE @SessionInfo nvarchar(max)
SELECT TOP 1 @SessionInfo = SessionInfo FROM t_Trade WHERE SessionId = @SessionId AND SessionInfo>''

DECLARE @RunningBalance float SET @RunningBalance = 0.00
DECLARE @RunningBalanceTotal float SET @RunningBalanceTotal = 0.00
UPDATE t_Trade SET 
	@RunningBalance = RunningBalance = (CASE WHEN @RunningBalance+ [GrossPL] < 0 THEN @RunningBalance+ [GrossPL] ELSE 0 END),
	@RunningBalanceTotal = RunningBalanceTotal = @RunningBalanceTotal+ [GrossPL],
	SessionInfo = ''
WHERE SessionId = @SessionId

UPDATE TOP (1) t_Trade SET SessionInfo = @SessionInfo WHERE SessionId = @SessionId

--ProcessTrades  @SessionId = '5F7FB6C8-DB47-4FD9-811D-0218B3000A4B'