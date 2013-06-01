CREATE PROCEDURE [dbo].[s_Session_Delete]
  @Uid uniqueidentifier
AS


DELETE t_Session 
WHERE t_Session.Uid = @Uid

DELETE FROM t_Trade WHERE SessionId = @Uid

SELECT @Uid DeletedSessionUid