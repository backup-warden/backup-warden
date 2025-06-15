using Serilog;
using Serilog.Events;
using System;
using System.IO;
using Windows.Storage;

namespace BackupWarden.Logging
{
    public static class SerilogConfigurator
    {
        public static void Configure(bool isMsix)
        {
            string logsDirectory;

            if (isMsix)
            {
                logsDirectory = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Logs");
            }
            else
            {
                var localAppDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                logsDirectory = Path.Combine(localAppDataPath, "BackupWarden", "Logs");
            }
            
            Directory.CreateDirectory(logsDirectory);

            string logFilePath = Path.Combine(logsDirectory, "log-.txt");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .Enrich.FromLogContext()
                .WriteTo.Debug(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                .WriteTo.File(
                    logFilePath,
                    rollingInterval: RollingInterval.Day,
                    restrictedToMinimumLevel: LogEventLevel.Information,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
        }
    }

}
