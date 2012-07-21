CREATE TABLE [dbo].[t_Trade] (
    [Id]                   NVARCHAR (16)    NOT NULL,
    [Buy]                  BIT              NOT NULL,
    [PL]                   FLOAT (53)       NOT NULL,
    [GrossPL]              FLOAT (53)       NOT NULL,
    [Lot]                  FLOAT (53)       NOT NULL,
    [Pair]                 NVARCHAR (7)     NOT NULL,
    [TimeOpen]             DATETIME         NOT NULL,
    [TimeClose]            DATETIME         NOT NULL,
    [AccountId]            NVARCHAR (16)    NOT NULL,
    [Commission]           FLOAT (53)       NOT NULL,
    [IsVirtual]            BIT              NOT NULL,
    [TimeStamp]            DATETIME         CONSTRAINT [DF_t_Trade_TimeStamp] DEFAULT (getdate()) NULL,
    [CorridorHeightInPips] FLOAT (53)       NOT NULL,
    [CorridorMinutesBack]  FLOAT (53)       NOT NULL,
    [SessionId]            UNIQUEIDENTIFIER NOT NULL,
    [PriceOpen]            FLOAT (53)       CONSTRAINT [DF_t_Trade_PriceOpen] DEFAULT ((0)) NOT NULL,
    [PriceClose]           FLOAT (53)       CONSTRAINT [DF_t_Trade_PriceClose] DEFAULT ((0)) NOT NULL,
    [SessionInfo]          NVARCHAR (4000)  NULL,
    [RunningBalance]       FLOAT (53)       NULL,
    [RunningBalanceTotal]  FLOAT (53)       NULL,
    CONSTRAINT [PK_t_Trade] PRIMARY KEY CLUSTERED ([Id] ASC)
);








GO
CREATE TRIGGER tri_Trade ON  t_Trade 
AFTER INSERT
AS 
BEGIN
SET NOCOUNT ON;

DECLARE @SessionId uniqueidentifier
SELECT @SessionId = SessionId FROM inserted

EXEC ProcessTrades  @SessionId = @SessionId--'E98D7FC3-A8A5-4B51-85CF-366362C57F50'
END
--DECLARE @SessionId uniqueidentifier SET @SessionId = '5F7FB6C8-DB47-4FD9-811D-0218B3000A4B'
--SELECT * FROM t_Trade WHERE SessionId = @SessionId
--BEGIN TRAN
--INSERT INTO [dbo].[t_Trade]           ([Id],[Buy],[PL],[GrossPL],[Lot],[Pair],[TimeOpen],[TimeClose],[AccountId],[Commission],[IsVirtual],[TimeStamp],[CorridorHeightInPips],[CorridorMinutesBack],[SessionId],[PriceOpen],[PriceClose],[SessionInfo],[RunningBalance])     VALUES           (63460439857934,1,0,0,0,'',getdate(),getdate(),'',0,0,getdate(),0,0,'5F7FB6C8-DB47-4FD9-811D-0218B3000A4B',0,0,'TradingDistanceFunction:RatesHeight,TakeProfitFunction:RatesHeight,ProfitToLossExitRatio_:3,ExtreamCloseOffset_:1,CurrentLossInPipsCloseAdjustment_:1,CorridorBigToSmallRatio_:-1.2,TradingAngleRange_:20,MaximumPositions_:100,BarPeriod:m5,BarsCount:600,SyncAll:False,StDevAverageLeewayRatio_:1,StDevTresholdIterations_:-2',NULL)
--SELECT * FROM t_Trade WHERE SessionId = '5F7FB6C8-DB47-4FD9-811D-0218B3000A4B'
--ROLLBACK