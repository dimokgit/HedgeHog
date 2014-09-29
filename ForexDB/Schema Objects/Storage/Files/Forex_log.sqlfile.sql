ALTER DATABASE [$(DatabaseName)]
    ADD LOG FILE (NAME = [Forex_log], FILENAME = '$(Path1)$(DatabaseName).ldf', MAXSIZE = 2097152 MB, FILEGROWTH = 10 %);

