using Renci.SshNet;

namespace LogViewer
{

  /// <summary>
  /// Лог файл.
  /// </summary>
  class LogFile
  {
    public string FullPath { get; }
    public string Name { get; }
    public bool IsLocal { get; }
    public SftpClient SftpClient { get; }

    public LogFile(string fullPath, string name)
    {
      this.FullPath = fullPath;
      this.Name = name;
      this.IsLocal = true;
    }

    public LogFile(string fullPath)
    {
      this.FullPath = fullPath;
      this.Name = System.IO.Path.GetFileName(fullPath);
      this.IsLocal = true;
    }

    public LogFile(string fullPath, SftpClient sftpClient)
    {
      this.FullPath = fullPath;
      this.Name = System.IO.Path.GetFileName(fullPath);
      this.IsLocal = false;
      this.SftpClient = sftpClient;
    }

  }
}
