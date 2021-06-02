using KotchatBot.Interfaces;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;

namespace KotchatBot.Core
{
    public class FolderImageSource : IRandomImageSource
    {
        private readonly Lazy<string[]> _allFiles;
        private readonly Random _rnd;
        private readonly Logger _log;
        private HashSet<int> _usedFiles;

        public string Command => ".random";

        public FolderImageSource(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("message", nameof(path));
            }

            if (!Directory.Exists(path))
            {
                throw new ArgumentException($"Directory {path} doesn't exist");
            }

            _log = LogManager.GetCurrentClassLogger();
            _allFiles = new Lazy<string[]>(() => ListImages(path));
            _usedFiles = new HashSet<int>();
            _rnd = new Random();
        }

        public async Task<string> NextFile()
        {
            while (_usedFiles.Count != _allFiles.Value.Length)
            {
                var index = _rnd.Next(0, _allFiles.Value.Length - 1);
                if (!_usedFiles.Contains(index))
                {
                    _usedFiles.Add(index);
                    return _allFiles.Value[index];
                }
            }

            _log.Info("All known files have been returned, starting to repeat already returned");
            _usedFiles.Clear();
            return await NextFile();
        }

        public async Task<string> NextFile(string parameter = null)
        {
            // this images source does not support 'parameter'
            return await NextFile();
        }

        private string[] ListImages(string path)
        {
            var list = new LinkedList<string>();
            ListImagesInternal(path, list);
            return list.ToArray();
        }

        private void ListImagesInternal(string directory, ICollection<string> foundFiles, bool includeSubdirs = true)
        {
            var images = new List<string>();
            try
            {
                images.AddRange(Directory.EnumerateFiles(directory, "*.*", SearchOption.TopDirectoryOnly));
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

            if (!includeSubdirs)
            {
                return;
            }

            IEnumerable<string> subDirs = new List<string>();
            try
            {
                subDirs = Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly);
            }
            catch (SecurityException) { }
            catch (UnauthorizedAccessException) { }
            catch (Exception ex)
            {
                _log.Error(ex, $"Directory enumeration failed in {directory}");
            }

            foreach (var subDir in subDirs)
            {
                ListImagesInternal(subDir, foundFiles, includeSubdirs);
            }
        }
    }
}