ALTER DATABASE [$(DatabaseName)]
    ADD FILE (NAME = [Forex], FILENAME = '$(Path2)$(DatabaseName).mdf', FILEGROWTH = 10 %) TO FILEGROUP [PRIMARY];

