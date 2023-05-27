using LogReader;
using Microsoft.Toolkit.Uwp.Notifications;
using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Renci.SshNet;
using System.Text.RegularExpressions;
using SshConfigParser;
using Renci.SshNet.Security;
using Key = System.Windows.Input.Key;

namespace LogViewer
{
  /// <summary>
  /// Interaction logic for MainWindow.xaml
  /// </summary>
  public partial class MainWindow : Window
  {
    public const string WindowTitle = "Directum Log Viewer";

    public const string NotificationError = "NotificationError";

    public const string NotificationTypeKey = "Type";

    public const string NotificationFilePathKey = "FilePath";

    public const string NotificationTimeKey = "Time";

    private const string OpenAction = "OpenAction";

    private const string OpenSshAction = "OpenSshAction";

    private const string All = "All";

    private const string IconFileName = "horse.png";

    private const int GridUpdatePeriod = 1000;

    private readonly List<LogHandler> logHandlers = new List<LogHandler>();

    private readonly ObservableCollection<LogLine> logLines = new ObservableCollection<LogLine>();

    private ObservableCollection<LogLine> filteredLogLines;

    private readonly Uri notifyLogo;

    private ICollectionView logLinesView;

    private LogWatcher logWatcher;

    private ScrollViewer gridScrollViewer;

    private LogFile openedLogFile;

    private readonly string[] hiddenColumns = { "Pid", "Trace", "Tenant" };

    private LogFile openSshLogFileObject = new LogFile(OpenSshAction, "Open from ssh-file...");
    private const string HandSshAction = "Ввести параметры вручную";
    private SftpClient sftpClient;
    private SshConfig sshConfig;

    public MainWindow()
    {
      InitializeComponent();

      DataContext = this;

      SettingsWindow.Load();

      if (SettingsWindow.IsFirstRun() && !ShowSettingsWindow())
      {
        Application.Current.Shutdown();
        return;
      }

      notifyLogo = GetNotifyLogo();

      if (!Directory.Exists(SettingsWindow.LogsPath) && !ShowSettingsWindow())
      {
        Application.Current.Shutdown();
        return;
      }

      var files = FindLogs(SettingsWindow.LogsPath);

      this.sshConfig = SshConfig.ParseFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "config"));

      InitControls(files);

      if (SettingsWindow.UseBackgroundNotification)
      {
        if (files != null)
          CreateHandlers(files);

        SetNotificationActivated();
      }

    }

    private void Window_ContentRendered(object sender, EventArgs e)
    {
      this.Title = WindowTitle;
      gridScrollViewer = GetScrollViewer(LogsGrid);

      var args = Environment.GetCommandLineArgs();
      if (args.Length > 1)
      {
        var fileName = args[1];

        if (File.Exists(fileName) && Path.GetExtension(fileName) == ".log")
          SelectFileToOpen(fileName);
      }
    }

    private bool ShowSettingsWindow()
    {
      var result = SettingsWindow.ShowSettingsDialog() == true;

      if (result)
        ApplySettings();

      return result;
    }

    private Uri GetNotifyLogo()
    {
      string directory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
      string imageFilePath = Path.Combine(directory, IconFileName);

      if (!File.Exists(imageFilePath))
      {
        ImageConverter converter = new ImageConverter();
        byte[] data = (byte[])converter.ConvertTo(Properties.Resources.horse, typeof(byte[]));
        File.WriteAllBytes(imageFilePath, data);
      }

      return new Uri(imageFilePath);
    }

    private string[] FindLogs(string directory)
    {
      if (!Directory.Exists(directory))
        return null;

      string[] allfiles = Directory.GetFiles(directory, "*.log", SearchOption.AllDirectories);

      string machineName = System.Environment.MachineName.ToLower();
      var currentDate = DateTime.Today.ToString("yyyy-MM-dd");

      var whiteList = SettingsWindow.WhitelistLogs.Split(new[] { '\r', '\n' })
        .Select(s => s.Trim().ToLower().Replace("${machinename}", machineName).Replace("${shortdate}", currentDate))
        .Where(s => !String.IsNullOrEmpty(s))
        .ToArray();

      return allfiles.Select(f => new LogFile(f))
        .Where(n => whiteList.Contains(System.IO.Path.GetFileNameWithoutExtension(n.Name.ToLower())))
        .Select(r => r.FullPath)
        .ToArray();
    }

    private void CreateHandlers(string[] files)
    {
      foreach (var file in files)
        Task.Run(() => logHandlers.Add(new LogHandler(file, notifyLogo)));
    }

    private void InitControls(string[] files)
    {
      LogsFileNames.Items.Clear();

      foreach (var file in files)
        LogsFileNames.Items.Add(new LogFile(file));

      LogsFileNames.Items.Add(new LogFile(OpenAction, "Open from file..."));

      InitTenantFilter();
      InitLevelFilter();

      logLinesView = CollectionViewSource.GetDefaultView(logLines);

      SSHVisibilityToggleBtn.IsChecked = false;
      SshConfig1.Visibility = Visibility.Collapsed;
      LogsFileNames.Items.Remove(openSshLogFileObject);

      Hosts.Items.Add(HandSshAction);
      Hosts.SelectedItem = HandSshAction;
      var hosts = this.sshConfig.FindHosts();
      foreach (var h in hosts)
        Hosts.Items.Add(h);
    }

    private void InitTenantFilter()
    {
      TenantFilter.Items.Clear();
      TenantFilter.Items.Add(All);
      TenantFilter.SelectedValue = All;
    }

    private void InitLevelFilter()
    {
      LevelFilter.Items.Clear();
      LevelFilter.Items.Add(All);
      LevelFilter.Items.Add("Trace");
      LevelFilter.Items.Add("Debug");
      LevelFilter.Items.Add("Info");
      LevelFilter.Items.Add("Warn");
      LevelFilter.Items.Add("Error");
      LevelFilter.Items.Add("Fatal");

      LevelFilter.SelectedValue = All;
    }

    private void SetNotificationActivated()
    {
      if (SettingsWindow.UseBackgroundNotification)
      {
        ToastNotificationManagerCompat.OnActivated += toastArgs =>
        {
          ToastArguments args = ToastArguments.Parse(toastArgs.Argument);

          var type = string.Empty;
          args.TryGetValue(NotificationTypeKey, out type);

          if (type == NotificationError)
          {
            var filePath = string.Empty;
            args.TryGetValue(NotificationFilePathKey, out filePath);

            var time = string.Empty;
            args.TryGetValue(NotificationTimeKey, out time);

            Application.Current.Dispatcher.Invoke(delegate
            {
              if (!LogsFileNames.IsEnabled)
                return;

              var selectedLog = (LogFile)LogsFileNames.SelectedItem;

              if (selectedLog == null || selectedLog.FullPath.ToLower() != filePath.ToLower())
              {
                var logWithError = LogsFileNames.Items.Cast<LogFile>().FirstOrDefault(i => i.FullPath.ToLower() == filePath.ToLower());

                if (logWithError == null)
                  return;

                LogsFileNames.SelectedItem = logWithError;
              }

              var dt = new DateTime(long.Parse(time));
              var itemWithError = logLines.FirstOrDefault(i => i.Level == LogHandler.LogLevelError && i.Time == dt);
              if (itemWithError != null)
              {
                BringToForeground();

                if (!string.IsNullOrEmpty(Filter.Text))
                  Filter.Text = null;

                if (LevelFilter.SelectedValue != All)
                  LevelFilter.SelectedValue = All;

                SetFilter(string.Empty, All, All);
                LogsGrid.SelectedItem = itemWithError;
                LogsGrid.ScrollIntoView(itemWithError);
                Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => LogsGrid.Focus()));
              }
            });
          }
        };
      }
    }

    private void CloseLogFile()
    {
      // Clear previous log resources
      if (logWatcher != null)
      {
        logWatcher.Dispose();
        logWatcher = null;
      }

      logLines.Clear();
      InitTenantFilter();
      InitLevelFilter();
      this.Title = WindowTitle;
      LogsGrid.ItemsSource = null;
      SearchGrid.ItemsSource = null;
      filteredLogLines = null;
      GC.Collect();
    }

    private void OpenLogFile(LogFile logFile)
    {
      try
      {
        LoadBar.Visibility = Visibility.Visible;
        LogsFileNames.IsEnabled = false;
        Filter.IsEnabled = false;
        LogsGrid.IsEnabled = false;
        ColumnVisibilityToggleBtn.IsEnabled = false;
        TenantFilter.IsEnabled = false;
        LevelFilter.IsEnabled = false;
        Filter.Clear();

        this.Title = string.Format($"{WindowTitle} ({logFile.FullPath})");
        LogsGrid.ItemsSource = null;
        filteredLogLines = null;

        if (logFile.IsLocal)
          logWatcher = new LogWatcher(logFile.FullPath);
        else
          logWatcher = new LogWatcher(logFile.FullPath, logFile.SftpClient);
        logWatcher.BlockNewLines += OnBlockNewLines;
        logWatcher.FileReCreated += OnFileReCreated;
        logWatcher.ReadToEndLine();
        LogsGrid.ItemsSource = logLines;
        LogsGrid.ScrollIntoView(logLines.Last());

        logWatcher.StartWatch(GridUpdatePeriod);

        var tenants = logLines.Where(l => !string.IsNullOrEmpty(l.Tenant)).Select(l => l.Tenant).Distinct().OrderBy(l => l);

        foreach (var tenant in tenants)
        {
          TenantFilter.Items.Add(tenant);
        }
      }
      catch (Exception e)
      {
        MessageBox.Show($"Error opening log from '{logFile.FullPath}'.\n{e.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
      }
      finally
      {
        LoadBar.Visibility = Visibility.Hidden;
        LogsFileNames.IsEnabled = true;
        Filter.IsEnabled = true;
        LogsGrid.IsEnabled = true;
        ColumnVisibilityToggleBtn.IsEnabled = true;
        TenantFilter.IsEnabled = true;
        LevelFilter.IsEnabled = true;
        GC.Collect();
      }
    }

    private void Files_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      var filterValue = Filter.Text;
      var levelValue = LevelFilter.SelectedValue;

      CloseLogFile();

      var comboBox = sender as ComboBox;

      LogFile selectedItem = comboBox.SelectedItem as LogFile;
      if (selectedItem == null)
      {
        this.TrayInfo.ToolTipText = this.SetToolTipText(string.Empty);
        return;
      }

      if (selectedItem.FullPath == OpenAction)
      {
        var dialog = new CommonOpenFileDialog
        {
          IsFolderPicker = false
        };

        dialog.Filters.Add(new CommonFileDialogFilter("Log Files (*.log)", ".log"));

        if (CommonFileDialogResult.Ok == dialog.ShowDialog())
          SelectFileToOpen(dialog.FileName);
        else
        {
          comboBox.SelectedItem = null;
          this.TrayInfo.ToolTipText = this.SetToolTipText(string.Empty);
        }

        return;
      }

      if (selectedItem.FullPath == OpenSshAction)
      {
        if (this.sftpClient != null)
        {
          var remoteFolder = this.RemoteFolder.Text;
          try
          {

            var files = this.sftpClient.ListDirectory(remoteFolder).Where(f => !f.IsDirectory).OrderByDescending(f => f.LastWriteTime);
            var dialog = new SelectRemoteFileWindow(files);
            dialog.RemoteFileList.ItemsSource = files;
            dialog.Owner = this;
            var result = dialog.ShowDialog();
            if (result ?? false)
              SelectSshFileToOpen(dialog.currentFile);
            else
              comboBox.SelectedItem = null;
          }
          catch (Renci.SshNet.Common.SftpPathNotFoundException)
          {
            MessageBox.Show(string.Format("Папка {0} на сервере {1} не найдена", remoteFolder, this.sftpClient.ConnectionInfo.Host),
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
          }
          catch (Exception ex)
          {
            MessageBox.Show(string.Format("Неизвестная ошибка {0} при подключении к папке {1} на сервере {2} не найдена",
                                          ex.Message, remoteFolder, this.sftpClient.ConnectionInfo.Host),
                            "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            MessageBox.Show(ex.StackTrace);
          }
        }
        else
        {
          comboBox.SelectedItem = null;
          MessageBox.Show("Need ssh-server connection", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        return;
      }

      comboBox.Items.Refresh();

      openedLogFile = selectedItem;
      OpenLogFile(selectedItem);

      Filter.Text = filterValue;
      this.TrayInfo.ToolTipText = this.SetToolTipText(selectedItem.Name);
      LevelFilter.SelectedValue = levelValue;
    }

    private string SetToolTipText(string filename)
    {
      if (filename == string.Empty)
        return "Directum Log Viewer";
      else
        return string.Format("Directum Log Viewer - {0}", filename);
    }

    private void OnBlockNewLines(List<string> lines, bool isEndFile, double progress)
    {
      var convertedLogLines = Converter.ConvertLinesToObjects(lines);

      Application.Current.Dispatcher.Invoke(
        new Action(() =>
        {
          if (LoadBar.Visibility == Visibility.Visible && LoadBar.Value != progress)
            LoadBar.Dispatcher.Invoke(() => LoadBar.Value = progress, DispatcherPriority.Background);

          var scrollToEnd = false;

          if (gridScrollViewer != null)
          {
            gridScrollViewer.UpdateLayout();

            if (gridScrollViewer.VerticalOffset == gridScrollViewer.ScrollableHeight)
              scrollToEnd = true;
          }

          foreach (var logLine in convertedLogLines)
          {
            logLines.Add(logLine);

            if (filteredLogLines != null)
            {
              var tenant = TenantFilter.SelectedValue as string;
              var level = LevelFilter.SelectedValue as string;

              if (NeedShowLine(logLine, Filter.Text, tenant, level))
                filteredLogLines.Add(logLine);
            }
          }

          if (scrollToEnd)
            LogsGrid.ScrollIntoView(convertedLogLines.Last());

        }));
    }

    private void OnFileReCreated()
    {
      Application.Current.Dispatcher.Invoke(new Action(() =>
      {
        CloseLogFile();
        if (openedLogFile != null)
          OpenLogFile(openedLogFile);
      }));
    }

    private void LogsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      var line = (sender as DataGrid).SelectedItem as LogLine;

      if (line == null)
        DetailText.Text = null;
      else
      {
        DetailText.Text = String.Empty;

        if (!String.IsNullOrEmpty(line.UserName))
          DetailText.Text += $"UserName: {line.UserName}\n";

        if (!String.IsNullOrEmpty(line.Tenant))
          DetailText.Text += $"Tenant: {line.Tenant}\n";

        if (!String.IsNullOrEmpty(line.Pid))
          DetailText.Text += $"Pid: {line.Pid}\n";

        if (!String.IsNullOrEmpty(line.Trace))
          DetailText.Text += $"Trace: {line.Trace}\n";

        if (!String.IsNullOrEmpty(line.Version))
          DetailText.Text += $"Version: {line.Version}\n";

        if (!String.IsNullOrEmpty(line.FullMessage))
        {
          if (!String.IsNullOrEmpty(DetailText.Text))
            DetailText.Text += "\n";

          DetailText.Text += line.FullMessage;
        }
      }
    }

    private void Settins_Click(object sender, RoutedEventArgs e)
    {
      if (ShowSettingsWindow())
      {
        // TODO сделать применение настроек без перезапуска приложения.
        MessageBox.Show("Settings will be applied after restarting the application");
        Application.Current.Shutdown();
      }
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
      Application.Current.Shutdown();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
      if (SettingsWindow.CloseToTray)
      {
        e.Cancel = true;
        this.Hide();
        base.OnClosing(e);
      }
    }

    private void TaskbarIcon_TrayLeftMouseUp(object sender, RoutedEventArgs e)
    {
      BringToForeground();
    }

    public void BringToForeground()
    {
      if (this.WindowState == WindowState.Minimized || this.Visibility == Visibility.Hidden)
      {
        this.Show();
        this.WindowState = WindowState.Maximized;
      }

      this.Activate();
      this.Topmost = true;
      this.Topmost = false;
      this.Focus();
    }

    private async void Filter_TextChanged(object sender, TextChangedEventArgs e)
    {
      TextBox tb = (TextBox)sender;
      int startLength = tb.Text.Length;

      await Task.Delay(1500);

      if (startLength == tb.Text.Length && tb.IsEnabled && e.UndoAction != UndoAction.Clear)
      {
        var tenant = TenantFilter.SelectedValue as string;
        var level = LevelFilter.SelectedValue as string;
        SetFilter(tb.Text, tenant, level);
      }
    }

    private bool NeedShowLine(LogLine line, string text, string tenant, string level)
    {
      var result = true;


      if (!string.IsNullOrEmpty(text))
      {
        try
        {
          Regex regex = new Regex(text, RegexOptions.IgnoreCase | RegexOptions.Singleline);
          if (this.UseRegex.IsChecked.Value)
            result = !string.IsNullOrEmpty(line.FullMessage) && regex.IsMatch(line.FullMessage);
          else
            result = !string.IsNullOrEmpty(line.FullMessage) && line.FullMessage.IndexOf(text, StringComparison.OrdinalIgnoreCase) > -1;
          result = result || (!string.IsNullOrEmpty(line.Trace) && line.Trace.IndexOf(text, StringComparison.OrdinalIgnoreCase) > -1) ||
                             (!string.IsNullOrEmpty(line.Pid) && line.Pid.IndexOf(text, StringComparison.OrdinalIgnoreCase) > -1) ||
                             (!string.IsNullOrEmpty(line.Level) && line.Level.IndexOf(text, StringComparison.OrdinalIgnoreCase) > -1);
        }
        catch (RegexParseException)
        {
          result = false;
        }
      }

      if (!string.IsNullOrEmpty(tenant) && !string.Equals(tenant, All, StringComparison.InvariantCultureIgnoreCase))
      {
        result = result && string.Equals(line.Tenant, tenant, StringComparison.InvariantCultureIgnoreCase);
      }

      if (!string.IsNullOrEmpty(level) && !string.Equals(level, All, StringComparison.InvariantCultureIgnoreCase))
      {
        result = result && line.Level != null && string.Equals(line.Level, level, StringComparison.InvariantCultureIgnoreCase);
      }

      return result;
    }

    private void SetFilter(string text, string tenant, string level)
    {
      if (logLinesView == null)
        return;

      var needFilter = !String.IsNullOrEmpty(text) ||
        (!String.Equals(tenant, All) && !String.IsNullOrEmpty(tenant)) ||
        (!String.Equals(level, All) && !String.IsNullOrEmpty(level));

      if (needFilter)
      {
        filteredLogLines = new ObservableCollection<LogLine>(logLines.Where(l => NeedShowLine(l, text, tenant, level)));
        LogsGrid.ItemsSource = filteredLogLines;
      }
      else
      {
        filteredLogLines = null;
        LogsGrid.ItemsSource = logLines;
      }

      if (LogsGrid.SelectedItem != null)
        LogsGrid.ScrollIntoView(LogsGrid.SelectedItem);

    }

    private void CopyCommand(object sender, ExecutedRoutedEventArgs e)
    {
      var sb = new StringBuilder();
      foreach (var item in LogsGrid.SelectedItems)
      {
        var logLine = (LogLine)item;
        var logLineElements = Converter.ConvertObjectToDict(logLine);
        sb.AppendLine(Converter.TsvFormat(logLineElements));
      }
      Clipboard.SetText(sb.ToString());
    }

    private void SearchText(Object sender, ExecutedRoutedEventArgs e)
    {
      if (!LogsFileNames.IsEnabled)
        return;

      var dialog = new SearchWindow();
      dialog.Owner = this;
      var result = dialog.ShowDialog();

      if (result == true)
      {
        var tenant = TenantFilter.SelectedValue as string;
        var level = LevelFilter.SelectedValue as string;

        SearchGrid.ItemsSource = logLines.Where(l => NeedShowLine(l, dialog.SearchText.Text, tenant, level)).ToList();
        BottomTabControl.SelectedItem = SearchTab;
      }
    }

    private void SearchGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      LogLine line = (sender as DataGrid).SelectedItem as LogLine;

      if (line != null)
      {
        LogsGrid.SelectedItem = line;
        LogsGrid.ScrollIntoView(line);
      }
    }

    private void FilterTenant_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      var tenant = (sender as ComboBox).SelectedItem as string;

      if (tenant != null)
      {
        var level = LevelFilter.SelectedValue as string;
        SetFilter(Filter.Text, tenant, level);
      }
    }

    private void FilterLevel_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      var level = (sender as ComboBox).SelectedItem as string;

      if (level != null)
      {
        var tenant = TenantFilter.SelectedValue as string;
        SetFilter(Filter.Text, tenant, level);
      }
    }

    private void SSHVisibilityCheck(object sender, RoutedEventArgs e)
    {
      SshConfig1.Visibility = Visibility.Visible;
      LogsFileNames.Items.Add(openSshLogFileObject);
    }

    private void SSHVisibilityUnchecked(object sender, RoutedEventArgs e)
    {
      SshConfig1.Visibility = Visibility.Collapsed;
      LogsFileNames.Items.Remove(openSshLogFileObject);
    }

    private void ColumnVisibilityCheck(object sender, RoutedEventArgs e)
    {
      foreach (var column in LogsGrid.Columns.Where(c => hiddenColumns.Contains(c.Header)))
      {
        column.Visibility = Visibility.Visible;
      }
    }

    private void ColumnVisibilityUnchecked(object sender, RoutedEventArgs e)
    {
      foreach (var columns in LogsGrid.Columns.Where(c => hiddenColumns.Contains(c.Header)))
      {
        columns.Visibility = Visibility.Collapsed;
      }
    }

    private void ApplySettings()
    {
      if (SettingsWindow.AssociateLogFileChanged)
      {
        if (SettingsWindow.AssociateLogFile == true)
          FileAssociations.SetAssociation();
        else
          FileAssociations.RemoveAssociation();
      }
    }

    private void SelectFileToOpen(string fileName)
    {
      var logFiles = LogsFileNames.Items.Cast<LogFile>().ToList();

      var logFile = logFiles.FirstOrDefault(l => string.Equals(l.FullPath, fileName, StringComparison.InvariantCultureIgnoreCase));

      if (logFile != null)
      {
        LogsFileNames.SelectedItem = logFile;
      }
      else
      {
        // Создать фоновый обработчик для нового файла.
        if (SettingsWindow.UseBackgroundNotification)
          logHandlers.Add(new LogHandler(fileName, notifyLogo));

        logFile = new LogFile(fileName);
        LogsFileNames.Items.Insert(LogsFileNames.Items.Count - 1, logFile);
        LogsFileNames.SelectedItem = logFile;
      }
    }

    private void SelectSshFileToOpen(string fileName)
    {
      var logFiles = LogsFileNames.Items.Cast<LogFile>().ToList();

      var logFile = logFiles.FirstOrDefault(l => string.Equals(l.FullPath, fileName, StringComparison.InvariantCultureIgnoreCase));

      if (logFile != null)
      {
        LogsFileNames.SelectedItem = logFile;
      }
      else
      {
        logFile = new LogFile(fileName, sftpClient);
        LogsFileNames.Items.Insert(LogsFileNames.Items.Count - 1, logFile);
        LogsFileNames.SelectedItem = logFile;
      }
    }

    private ScrollViewer GetScrollViewer(UIElement element)
    {
      if (element == null)
        return null;

      ScrollViewer result = null;
      for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element) && result == null; i++)
      {
        if (VisualTreeHelper.GetChild(element, i) is ScrollViewer)
          result = (ScrollViewer)(VisualTreeHelper.GetChild(element, i));
        else
          result = GetScrollViewer(VisualTreeHelper.GetChild(element, i) as UIElement);
      }

      return result;
    }

    #region Убираем авто-скрол при клике по колонке Message или нажатии навигационных кнопок(up/down/pageup/pagedown) на клавиатуре.

    private void LogsGrid_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
      e.Handled = !(e.Source is System.Windows.Controls.DataGridRow);
    }

    private void LogsGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
      if (gridScrollViewer != null)
        // Делаем свои обработчики на кнопки.
        NavigationKeyDown(gridScrollViewer, e);
    }

    private void NavigationKeyDown(ScrollViewer scrollViewer, KeyEventArgs e)
    {
      bool controlDown = ((e.KeyboardDevice.Modifiers & ModifierKeys.Control) != 0);
      bool altDown = ((e.KeyboardDevice.Modifiers & ModifierKeys.Alt) != 0);

      if (!altDown)
      {
        bool invertForRTL = (FlowDirection == FlowDirection.RightToLeft);
        switch (e.Key)
        {
          case Key.Left:
            if (invertForRTL)
              scrollViewer.LineRight();
            else
              scrollViewer.LineLeft();
            e.Handled = true;
            break;

          case Key.Right:
            if (invertForRTL)
              scrollViewer.LineLeft();
            else
              scrollViewer.LineRight();
            e.Handled = true;
            break;

          case Key.Up:
            if (LogsGrid.SelectedIndex != -1)
            {
              if (LogsGrid.SelectedIndex > 0)
                LogsGrid.SelectedItem = LogsGrid.Items[LogsGrid.SelectedIndex - 1];

              LogsGrid.ScrollIntoView(LogsGrid.SelectedItem);

              DataGridRow row = (DataGridRow)LogsGrid.ItemContainerGenerator.ContainerFromIndex(LogsGrid.SelectedIndex);
              row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Up));
            }
            e.Handled = true;
            break;

          case Key.Down:
            if (LogsGrid.SelectedIndex != -1)
            {
              if (LogsGrid.SelectedIndex + 1 < LogsGrid.Items.Count)
                LogsGrid.SelectedItem = LogsGrid.Items[LogsGrid.SelectedIndex + 1];

              LogsGrid.ScrollIntoView(LogsGrid.SelectedItem);

              DataGridRow row = (DataGridRow)LogsGrid.ItemContainerGenerator.ContainerFromIndex(LogsGrid.SelectedIndex);
              row.MoveFocus(new TraversalRequest(FocusNavigationDirection.Down));
            }
            e.Handled = true;
            break;

          case Key.PageUp:
          case Key.PageDown:
            OnPageUpOrDownKeyDown(scrollViewer, e);
            break;

          case Key.Home:
            if (controlDown)
            {
              scrollViewer.ScrollToTop();

              if (LogsGrid.Items.Count > 0)
                LogsGrid.SelectedItem = LogsGrid.Items[0];
            }
            else
              scrollViewer.ScrollToLeftEnd();
            e.Handled = true;
            break;

          case Key.End:
            if (controlDown)
            {
              scrollViewer.ScrollToBottom();

              if (LogsGrid.Items.Count > 0)
                LogsGrid.SelectedItem = LogsGrid.Items[LogsGrid.Items.Count - 1];
            }
            else
              scrollViewer.ScrollToRightEnd();
            e.Handled = true;
            break;
        }
      }
    }

    private void OnPageUpOrDownKeyDown(ScrollViewer scrollHost, KeyEventArgs e)
    {
      if (scrollHost != null)
      {
        e.Handled = true;

        int rowIndex = LogsGrid.SelectedIndex;
        if (rowIndex >= 0)
        {
          int jumpDistance = Math.Max(1, (int)scrollHost.ViewportHeight - 1);
          int targetIndex = (e.Key == Key.PageUp) ? rowIndex - jumpDistance : rowIndex + jumpDistance;
          targetIndex = Math.Max(0, Math.Min(targetIndex, LogsGrid.Items.Count - 1));

          LogsGrid.SelectedItem = LogsGrid.Items[targetIndex];
          LogsGrid.ScrollIntoView(LogsGrid.SelectedItem);

          FocusNavigationDirection direction = e.Key == Key.PageUp ? FocusNavigationDirection.Up : FocusNavigationDirection.Down;
          DataGridRow row = (DataGridRow)LogsGrid.ItemContainerGenerator.ContainerFromIndex(LogsGrid.SelectedIndex);
          row.MoveFocus(new TraversalRequest(direction));
        }
      }
    }
    #endregion

    private void UseRegex_Changed()
    {
      int startLength = this.Filter.Text.Length;
      if (startLength == this.Filter.Text.Length && this.Filter.IsEnabled)
      {
        var tenant = this.TenantFilter.SelectedValue as string;
        var level = this.LevelFilter.SelectedValue as string;
        this.SetFilter(this.Filter.Text, tenant, level);
      }
    }

    private void UseRegex_Checked(object sender, RoutedEventArgs e)
    {
      this.UseRegex_Changed();
    }

    private void UseRegex_Unchecked(object sender, RoutedEventArgs e)
    {
      this.UseRegex_Changed();
    }

    private void SshHost_TextChanged(object sender, TextChangedEventArgs e)
    {
      this.SshConnectButton.Content = "Connect";
      this.SshConnectButton.IsEnabled = true;
    }

    private void SshPort_TextChanged(object sender, TextChangedEventArgs e)
    {
      this.SshConnectButton.Content = "Connect";
      this.SshConnectButton.IsEnabled = true;
    }

    private void SshLogin_TextChanged(object sender, TextChangedEventArgs e)
    {
      this.SshConnectButton.Content = "Connect";
      this.SshConnectButton.IsEnabled = true;
    }

    private void SshConnectButton_Click(object sender, RoutedEventArgs e)
    {
      try
      {
        ConnectionInfo connectionInfo;
        string host;

        if (this.Hosts.Text == HandSshAction)
        {
          host = this.SshHost.Text;
          var port = int.Parse(this.SshPort.Text);
          var login = this.SshLogin.Text;
          var password = this.SshPassword.Password;

          var methods = new List<AuthenticationMethod> { new PasswordAuthenticationMethod(login, password) };
          connectionInfo = new ConnectionInfo(host, port, login, methods.ToArray());
        }
        else
        {
          var hostInfo = this.sshConfig.Compute(this.Hosts.Text);

          host = hostInfo.HostName;
          var port = int.Parse(hostInfo.Port);
          var login = hostInfo.User;
          var password = this.SshPassword.Password;

          var identityFile = hostInfo.IdentityFile;
          PrivateKeyFile keyFile;
          if (password == "")
            keyFile = new PrivateKeyFile(identityFile);
          else
            keyFile = new PrivateKeyFile(identityFile, password);
          var keyFiles = new[] { keyFile };

          var methods = new List<AuthenticationMethod>{ new PrivateKeyAuthenticationMethod(login, keyFiles) };

          connectionInfo = new ConnectionInfo(host, port, login, methods.ToArray());
        }

        SftpClient sftpClient = new SftpClient(connectionInfo);
        this.sftpClient = sftpClient;
        this.sftpClient.Connect();
        this.SshConnectButton.Content = "Connected";
        this.SshConnectButton.IsEnabled = false;
        //MessageBox.Show(string.Format("Соединение с {0} установлено.", host), "", MessageBoxButton.OK, MessageBoxImage.Information);
      }
      catch (Exception ex)
      {
        MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
      }
    }

    private void Hosts_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      var comboBox = sender as ComboBox;

      string selectedItem = comboBox.SelectedItem as string;

      if (selectedItem == null)
      {
        this.TrayInfo.ToolTipText = this.SetToolTipText(string.Empty);
        return;
      }

      if (selectedItem == HandSshAction)
      {
        this.SshHost.IsEnabled = true;
        this.SshHost.Text = "";
        this.SshPort.IsEnabled = true;
        this.SshPort.Text = "";
        this.SshLogin.IsEnabled = true;
        this.SshLogin.Text = "";
      }
      else
      {
        var hostInfo = this.sshConfig.Compute(selectedItem);

        this.SshHost.IsEnabled = false;
        this.SshHost.Text = hostInfo.HostName;
        this.SshPort.IsEnabled = false;
        this.SshPort.Text = hostInfo.Port;
        this.SshLogin.IsEnabled = false;
        this.SshLogin.Text = hostInfo.User;
      }
      this.SshConnectButton.Content = "Connect";
      this.SshConnectButton.IsEnabled = true;

    }
  }
}
