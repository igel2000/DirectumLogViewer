using System;
using System.Collections.Generic;
using System.IO;
using LogReader;
using Microsoft.Toolkit.Uwp.Notifications;
using Renci.SshNet;

namespace LogViewer
{
  /// <summary>
  /// Обработчик лог файла.
  /// </summary>
  class LogHandler
  {
    public const string LogLevelError = "Error";
    private const int NotificationTextMaxLength = 200;
    private const int WatchPeriod = 3000;

    private readonly string filePath;
    private readonly Uri icon;
    private readonly string fileName;
    private readonly LogWatcher watcher;

    public LogHandler(string filePath, Uri icon)
    {
      this.filePath = filePath;
      this.icon = icon;
      fileName = Path.GetFileName(filePath);

      watcher = new LogWatcher(filePath);
      watcher.BlockNewLines += OnBlockNewLines;
      watcher.ReadToEndLine();
      watcher.StartWatch(WatchPeriod);
    }

    private void OnBlockNewLines(List<string> lines, bool isEndFile, double progress)
    {
      if (watcher.IsStartedWatching)
      {
        var convertedLogLines = Converter.ConvertLinesToObjects(lines);

        foreach (var logLine in convertedLogLines)
        {
          if (logLine.Level == LogLevelError)
          {
            try
            {
              var truncatedMessage = logLine.Message.Substring(0, Math.Min(NotificationTextMaxLength, logLine.Message.Length));

              new ToastContentBuilder()
                  .AddArgument(MainWindow.NotificationTypeKey, MainWindow.NotificationError)
                  .AddArgument(MainWindow.NotificationFilePathKey, filePath)
                  .AddArgument(MainWindow.NotificationTimeKey, logLine.Time.Ticks.ToString())
                  .AddAppLogoOverride(icon, ToastGenericAppLogoCrop.Circle)
                  .AddText(fileName)
                  .AddText(truncatedMessage)
                  .Show();
            }
            catch
            {
              // TODO не всегда приходят уведомлялки
            }
          }
        }
      }
    }
  }
}
