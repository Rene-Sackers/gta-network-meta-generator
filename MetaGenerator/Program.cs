using System;
using System.IO;
using System.Windows.Forms;

namespace MetaGenerator
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length == 1)
            {
                WatchDirectory(args[0]);
                return;
            }

            ShowFolderBrowser();
        }

        static void ShowFolderBrowser()
        {
            var folderBrowser = new FolderBrowserDialog();
            if (folderBrowser.ShowDialog() != DialogResult.OK) return;

            WatchDirectory(folderBrowser.SelectedPath);
        }

        static void WatchDirectory(string path)
        {
            if (!Directory.Exists(path)) {
                WriteLogMessage("Invalid path: " + path);
                return;
            }

            var resourceWatcher = new ResourceWatcher(path);
            Console.CancelKeyPress += (object sender, ConsoleCancelEventArgs e) => { resourceWatcher.StopWatch(); };
            resourceWatcher.StartWatch().Wait();
        }

        public static void WriteLogMessage(string message)
        {
            Console.WriteLine($"[{DateTime.Now.ToLongTimeString()}] {message}");
        }
    }
}
