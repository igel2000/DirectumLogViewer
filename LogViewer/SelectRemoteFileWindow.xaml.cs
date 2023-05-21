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

namespace LogViewer
{

  /// <summary>
  /// Interaction logic for Window1.xaml
  /// </summary>
  public partial class SelectRemoteFileWindow : Window
  {
    private string _lastColemunNameClicked = "Updated";
    private ListSortDirection _lastDirection = ListSortDirection.Ascending;

    private IEnumerable<Renci.SshNet.Sftp.SftpFile> files;

    public string currentFile = "";

    public SelectRemoteFileWindow(IEnumerable<Renci.SshNet.Sftp.SftpFile> files)
    {
      this.files = files;
      InitializeComponent();
      this.RemoteFileList.SelectedValue = this.files.First();
    }

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
            this.RemoteFileList.ItemsSource = files.OrderBy(f => f.Name);
          else
            this.RemoteFileList.ItemsSource = files.OrderByDescending(f => f.Name);
          _lastColemunNameClicked = columnNameForSort;
          break;
        case "Updated":
          if (_lastDirection == ListSortDirection.Ascending)
            this.RemoteFileList.ItemsSource = files.OrderBy(f => f.LastWriteTime);
          else
            this.RemoteFileList.ItemsSource = files.OrderByDescending(f => f.LastWriteTime);
          _lastColemunNameClicked = columnNameForSort;
          break;
        case "Size":
          if (_lastDirection == ListSortDirection.Ascending)
            this.RemoteFileList.ItemsSource = files.OrderBy(f => f.Length);
          else
            this.RemoteFileList.ItemsSource = files.OrderByDescending(f => f.Length);
          _lastColemunNameClicked = columnNameForSort;
          break;
      }
    }

    private void RemoteFileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
      if (e.RemovedItems.Count > 0)
      {
        var f = (Renci.SshNet.Sftp.SftpFile)e.AddedItems[0];
        this.currentFile = f.FullName;
      }
    }

    private void RemoteFileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
      this.DialogResult = true;
    }

    private void RemoteFileList_KeyDown(object sender, KeyEventArgs e)
    {
      if (e.Key == Key.Escape)
        this.DialogResult = false;
      if (e.Key == Key.Enter)
        this.DialogResult = true;
    }

  }
}
