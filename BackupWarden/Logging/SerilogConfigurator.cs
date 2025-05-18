using Serilog;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace BackupWarden.Logging
{
    public static class SerilogConfigurator
    {
        public static void Configure()
        {
            // Ensure the Logs directory exists in the LocalFolder
            string logsDirectory = Path.Combine(ApplicationData.Current.LocalFolder.Path, "Logs");
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
