using KotchatBot.Interfaces;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace KotchatBot.Core
{
    public class FolderImageSource : IRandomImageSource
    {
        private string[] _allFiles;
        private HashSet<int> _usedFiles;
        private Random _rnd;
        private readonly Logger _log;

        public string Command => ".random";

        public FolderImageSource(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("message", nameof(path));
            }

            if (!System.IO.Directory.Exists(path))
            {
                throw new ArgumentException($"Directory {path} doesn't exist");
            }

            _log = LogManager.GetCurrentClassLogger();

            var start = DateTime.UtcNow;
            _allFiles = ListImages(path); // TODO make lazy
            var finish = DateTime.UtcNow;
            _log.Info($"Listing files took {(finish - start).TotalSeconds} seconds, found {_allFiles.Length} files");

            _usedFiles = new HashSet<int>();
            _rnd = new Random();
        }

        private string[] ListImages(string path)
        {
            var list = new LinkedList<string>();
            ListImagesInternal(path, list);
            return list.ToArray();
        }

        private void ListImagesInternal(string directory, ICollection<string> foundFiles,
            bool includeSubdirs = true, CancellationToken ct = default(CancellationToken))
        {
            var images = new List<string>();
            try
            {
                images.AddRange(System.IO.Directory.EnumerateFiles(directory, "*.*", System.IO.SearchOption.TopDirectoryOnly));
            }
            catch (SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (Exception ex)
            {
                _log.Error(ex, $"Files enumeration failed in {directory}");
            }

            for (int i = 0; i < images.Count; ++i)
            {
                foundFiles.Add(images[i]);
            }
            images.Clear();
            images = null;

            if (!includeSubdirs || ct.IsCancellationRequested)
                return;

            IEnumerable<string> subDirs = new List<string>();
            try
            {
                subDirs = System.IO.Directory.EnumerateDirectories(directory, "*", System.IO.SearchOption.TopDirectoryOnly);
            }
            catch (SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (Exception ex)
            {
                _log.Error(ex, $"Directory enumeration failed in {directory}");
            }

            foreach (var subDir in subDirs)
                ListImagesInternal(subDir, foundFiles, includeSubdirs, ct);
        }

        public async Task<string> NextFile()
        {
            while (_usedFiles.Count != _allFiles.Length)
            {
                var index = _rnd.Next(0, _allFiles.Length - 1);
                if (!_usedFiles.Contains(index))
                {
                    _usedFiles.Add(index);
                    return _allFiles[index];
                }
            }
            // start all over again
            _log.Info("All known files have been returned, starting to repeat already returned");
            _usedFiles.Clear();
            return await NextFile();
        }
    }
}