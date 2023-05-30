using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Renci.SshNet;
using System.Collections;
using System.Collections.ObjectModel;

namespace LogViewer
{

  /// <summary>
  /// Interaction logic for Window1.xaml
  /// </summary>
  public partial class SelectRemoteFileWindow : Window
  {
    private const string WindowTitle = "Выбор лога";
    private string _lastColemunNameClicked = "Updated";
    private ListSortDirection _lastDirection = ListSortDirection.Ascending;

    private readonly IEnumerable<Renci.SshNet.Sftp.SftpFile> sftpFiles;

    public string currentFile = "";

    private ICollectionView filesView;

    private ObservableCollection<Renci.SshNet.Sftp.SftpFile> fileLines = new ObservableCollection<Renci.SshNet.Sftp.SftpFile>();

    private ObservableCollection<Renci.SshNet.Sftp.SftpFile> filteredFiles;

    private ScrollViewer gridScrollViewer;

    public SelectRemoteFileWindow(IEnumerable<Renci.SshNet.Sftp.SftpFile> files)
    {
      InitializeComponent();

      DataContext = this;

      InitControls(files);
    }

    private void Window_ContentRendered(object sender, EventArgs e)
    {
      //this.Title = WindowTitle;

      gridScrollViewer = GetScrollViewer(RemoteFileList);
    }

    private void InitControls(IEnumerable<Renci.SshNet.Sftp.SftpFile> files)
    {
      FilterFiles.Text = "";

      foreach (var f in files)
        this.fileLines.Add(f);
      this.filteredFiles = null;
      this.RemoteFileList.ItemsSource = this.fileLines;
      if (this.fileLines.Count > 0)
      {
        this.RemoteFileList.SelectedValue = this.fileLines.First();
        this.RemoteFileList.ScrollIntoView(this.fileLines.First());
      }

      filesView = CollectionViewSource.GetDefaultView(fileLines);
    }
    /*
    // Header click event
    void RemoteFileListHeader_Click(object sender, RoutedEventArgs e)
    {
      GridViewColumnHeader headerClicked = e.OriginalSource as GridViewColumnHeader;
      ListSortDirection direction;

      var columnNameForSort = headerClicked.Column.Header.ToString();
      if (columnNameForSort != _lastColemunNameClicked)
      {
        _lastColemunNameClicked = columnNameForSort;
        _lastDirection = ListSortDirection.Descending;
      }
      if (_lastDirection == ListSortDirection.Descending)
        _lastDirection = ListSortDirection.Ascending;
      else
        _lastDirection = ListSortDirection.Descending;
      switch (columnNameForSort)
      {
        case "Name":
          if (_lastDirection == ListSortDirection.Ascending)
            this.RemoteFileList.ItemsSource = fileLines.OrderBy(f => f.Name);
          else
            this.RemoteFileList.ItemsSource = fileLines.OrderByDescending(f => f.Name);
          _lastColemunNameClicked = columnNameForSort;
          break;
        case "Updated":
          if (_lastDirection == ListSortDirection.Ascending)
            this.RemoteFileList.ItemsSource = fileLines.OrderBy(f => f.LastWriteTime);
          else
            this.RemoteFileList.ItemsSource = fileLines.OrderByDescending(f => f.LastWriteTime);
          _lastColemunNameClicked = columnNameForSort;
          break;
        case "Size":
          if (_lastDirection == ListSortDirection.Ascending)
            this.RemoteFileList.ItemsSource = fileLines.OrderBy(f => f.Length);
          else
            this.RemoteFileList.ItemsSource = fileLines.OrderByDescending(f => f.Length);
          _lastColemunNameClicked = columnNameForSort;
          break;
      }
      
    }

    private void RemoteFileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      MessageBox.Show("RemoteFileList_SelectionChanged");
      if (e.RemovedItems.Count > 0)
      {
        var f = (Renci.SshNet.Sftp.SftpFile)e.AddedItems[0];
        this.currentFile = f.FullName;
      }
    }
    */
    private void RemoteFileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
      var selectedItem = this.RemoteFileList.SelectedValue as Renci.SshNet.Sftp.SftpFile;
      this.currentFile = selectedItem.FullName;
      this.DialogResult = true;
    }

    private async void Filter_TextChanged(object sender, TextChangedEventArgs e)
    {
      TextBox tb = (TextBox)sender;
      int startLength = tb.Text.Length;

      await Task.Delay(1500);

      if (startLength == tb.Text.Length && tb.IsEnabled && e.UndoAction != UndoAction.Clear)
      {
        SetFilter(tb.Text);
      }
    }


    private bool NeedAddFile(string fileName, string[] searchStrings)
    {
      foreach (var s in searchStrings)
        if (!fileName.Contains(s))
          return false;
      return true;
    }
    private void SetFilter(string text)
    {
      if (text != string.Empty && text != "")
      {
        var searchStrings = text.ToLower().Split(" ");
        var files = this.fileLines.Where(f => this.NeedAddFile(f.Name.ToLower(), searchStrings));
        this.filteredFiles = new ObservableCollection<Renci.SshNet.Sftp.SftpFile>(files);
        this.RemoteFileList.ItemsSource = this.filteredFiles;
        if (this.filteredFiles.Count() > 0)
          this.RemoteFileList.SelectedValue = this.filteredFiles.First();
        else
          this.RemoteFileList.SelectedValue = null;
      }
      else
      {
        this.filteredFiles = null;
        this.RemoteFileList.ItemsSource = this.fileLines;
        this.RemoteFileList.SelectedValue = this.fileLines.First();
      }
      if (this.RemoteFileList.SelectedItem != null)
        this.RemoteFileList.ScrollIntoView(this.RemoteFileList.SelectedItem);
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

    private void CommandBinding_Executed(object sender, ExecutedRoutedEventArgs e)
    {
      var selectedItem = this.RemoteFileList.SelectedValue as Renci.SshNet.Sftp.SftpFile;
      this.currentFile = selectedItem.FullName;
      this.DialogResult = true;

    }

    private void RemoteFileList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
      var u = e.OriginalSource as UIElement;
      if (e.Key == Key.Enter && u != null)
      {
        e.Handled = true;
        var selectedItem = this.RemoteFileList.SelectedValue as Renci.SshNet.Sftp.SftpFile;
        this.currentFile = selectedItem.FullName;
        this.DialogResult = true;
      }
      if (e.Key == Key.Escape)
        this.DialogResult = false;
      if (e.KeyboardDevice.IsKeyDown(Key.LeftCtrl) && e.Key == Key.L)
      {
        FocusManager.SetFocusedElement(this, FilterFiles);
        Keyboard.Focus(FilterFiles);
      }
    }
  }
}
