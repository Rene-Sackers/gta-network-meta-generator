using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace MetaGenerator
{

    public class ResourceWatcher
    {
        private const int RegenerationTimeout = 1000;

        private DirectoryInfo _directoryInfo;
        private string _metaFilePath;
        private FileSystemWatcher _watcher;
        private TaskCompletionSource<bool> _stopWatchTaskCompletion;
        private Task _metaRegenerationTask;
        private CancellationTokenSource _metaRegenerationTaskCancellationTokenSource;

        private FileExtensionMapping[] _fileExtensionMappings = new[]
        {
            new FileExtensionMapping(".cs", FileTypes.CSharp),
            new FileExtensionMapping(".dll", FileTypes.Compiled),
            new FileExtensionMapping(".pdb", FileTypes.Ignore),
            new FileExtensionMapping(".config", FileTypes.Ignore),
            new FileExtensionMapping(".js", FileTypes.JavaScript),
            new FileExtensionMapping(".js.map", FileTypes.Ignore),
            new FileExtensionMapping(".ts", FileTypes.Ignore),
            new FileExtensionMapping(".scss", FileTypes.Ignore),
        };

        private FileExtensionMapping _defaultFileExtensionMapping = new FileExtensionMapping("*.*", FileTypes.File);

        private string[] _generatedXmlTagNames = new[] { "script", "assembly", "file" };

        public ResourceWatcher(string path)
        {
            this._directoryInfo = new DirectoryInfo(path);
            this._metaFilePath = Path.Combine(_directoryInfo.FullName, "meta.xml");
        }

        public async Task StartWatch()
        {
            _watcher = new FileSystemWatcher(_directoryInfo.FullName);

            _watcher.Created += WatcherFileChanged;
            _watcher.Deleted += WatcherFileChanged;
            _watcher.Renamed += WatcherFileChanged;

            _watcher.IncludeSubdirectories = true;
            _watcher.EnableRaisingEvents = true;

            Program.WriteLogMessage("Watching directory: " + _directoryInfo.FullName);

            await QueueMetaGeneration();

            _stopWatchTaskCompletion = new TaskCompletionSource<bool>();
            await _stopWatchTaskCompletion.Task;
        }

        public async void StopWatch()
        {
            if (_stopWatchTaskCompletion == null) return;

            _watcher.Dispose();
            _stopWatchTaskCompletion.TrySetResult(true);
            await CancelQueuedMetaGenerationTask();
        }

        private async void WatcherFileChanged(object sender, FileSystemEventArgs e)
        {
            Program.WriteLogMessage($"File changed: {e.FullPath}, change type: {e.ChangeType}");

            await QueueMetaGeneration();
        }

        private async Task CancelQueuedMetaGenerationTask()
        {
            if (_metaRegenerationTask?.IsCompleted == false && _metaRegenerationTaskCancellationTokenSource != null)
            {
                if (_metaRegenerationTaskCancellationTokenSource.Token.IsCancellationRequested)
                {
                    _metaRegenerationTaskCancellationTokenSource.Cancel();
                }

                await _metaRegenerationTask;
                return;
            }

            return;
        }

        private async Task QueueMetaGeneration()
        {
            await CancelQueuedMetaGenerationTask();

            _metaRegenerationTaskCancellationTokenSource = new CancellationTokenSource();
            _metaRegenerationTask = Task.Factory.StartNew(RegenerateMetaFile, _metaRegenerationTaskCancellationTokenSource.Token);
            Program.WriteLogMessage("Queued regeneration task.");
        }

        private async void RegenerateMetaFile()
        {
            await Task.Delay(RegenerationTimeout);

            if (_metaRegenerationTaskCancellationTokenSource.Token.IsCancellationRequested)
            {
                Program.WriteLogMessage("Regeneration task cancellation requested.");
                return;
            }

            Program.WriteLogMessage("Regenerating meta file.");

            var currentRootNodes = GetCurrentRootNodes();

            var xmlDocumentRoot = new XElement("meta");
            var newXmlDocument = new XDocument(xmlDocumentRoot);

            xmlDocumentRoot.Add(currentRootNodes);

            var files = GetFilesRecursive(_directoryInfo);
            foreach (var file in files)
            {
                if (file.FullName == _metaFilePath) continue;

                xmlDocumentRoot.Add(CreateXmlNodeForFile(file));
            }
            
            Program.WriteLogMessage("Writing XML file to: " + _metaFilePath);

            WriteXmlFile(newXmlDocument, _metaFilePath);

            Program.WriteLogMessage("Done regenerating.");
        }

        private static void WriteXmlFile(XDocument newXmlDocument, string savePath)
        {
            using (var metaXmlFileStream = File.Create(savePath))
            {
                var xmlWriterSettings = new XmlWriterSettings { OmitXmlDeclaration = true, CloseOutput = true, Indent = true };

                using (var xmlWriter = XmlWriter.Create(metaXmlFileStream, xmlWriterSettings))
                {
                    newXmlDocument.WriteTo(xmlWriter);
                    metaXmlFileStream.Flush();
                }
            }
        }

        private XNode CreateXmlNodeForFile(FileInfo fileInfo)
        {
            var mappingType = GetFileMappingType(fileInfo);
            var relativePath = GetRelativePath(fileInfo);

            Program.WriteLogMessage($"Creating node for file: {relativePath}, mapping type: {mappingType.FileType}");
            
            switch (mappingType.FileType)
            {
                case FileTypes.Compiled: //<script src="Bin/Fuel.dll" type="server" lang="compiled" />
                    return new XElement("script", new XAttribute("src", relativePath), new XAttribute("type", "server"), new XAttribute("lang", "compiled"));
                case FileTypes.CSharp: //<script src="freeroam.cs" type="server" lang="csharp" />
                    return new XElement("script", new XAttribute("src", relativePath), new XAttribute("type", "server"), new XAttribute("lang", "csharp"));
                case FileTypes.JavaScript: //<script src="freeroam_local.js" type="client" lang="javascript" />
                    return new XElement("script", new XAttribute("src", relativePath), new XAttribute("type", "client"), new XAttribute("lang", "javascript"));
                case FileTypes.File: //<file src="skeletor.png" />
                    return new XElement("file", new XAttribute("src", relativePath));
                case FileTypes.Ignore:
                default:
                    return null;
            }
        }

        private IEnumerable<XNode> GetCurrentRootNodes()
        {
            var metaFileExists = File.Exists(_metaFilePath);
            if (!metaFileExists) return new XNode[0];

            return XDocument.Load(_metaFilePath)
                .Root
                .Nodes()
                .Where(n => !(n is XElement) || !_generatedXmlTagNames.Any(gtn => string.Equals(((XElement)n).Name.LocalName, gtn, StringComparison.OrdinalIgnoreCase)));
        }

        private string GetRelativePath(FileInfo file)
        {
            var relativePath = file.FullName.Replace(_directoryInfo.FullName, "");
            return relativePath.Replace("\\", "/").TrimStart('/');
        }

        private FileExtensionMapping GetFileMappingType(FileInfo file)
        {
            var knwonFileExtensionMapping = _fileExtensionMappings.FirstOrDefault(fem => file.Name.ToLowerInvariant().EndsWith(fem.Extension.ToLowerInvariant()));

            return knwonFileExtensionMapping ?? _defaultFileExtensionMapping;
        }

        private IEnumerable<FileInfo> GetFilesRecursive(DirectoryInfo currentDirectory)
        {
            foreach (var file in currentDirectory.GetFiles())
            {
                yield return file;
            }

            foreach (var subDirectory in currentDirectory.GetDirectories())
            {
                foreach (var subDirectoryFile in GetFilesRecursive(subDirectory))
                {
                    yield return subDirectoryFile;
                }
            }
        }
    }
}
