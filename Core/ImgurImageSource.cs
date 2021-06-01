using KotchatBot.Dto;
using KotchatBot.Interfaces;
using Newtonsoft.Json;
using NLog;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace KotchatBot.Core
{
    public class ImgurImageSource : IRandomImageSource
    {
        private readonly string _clientId;
        private readonly string[] _tags;
        private readonly IDataStorage _dataSource;
        private readonly Logger _log;
        private readonly HttpClient _httpClient;
        private Random _rnd;
        private bool _isInitialized = false; // TODO refactor

        public string Command => ".imgur";

        public ImgurImageSource(string clientId, string[] tags, IDataStorage ds)
        {
            if (string.IsNullOrEmpty(clientId))
            {
                throw new ArgumentException("message", nameof(clientId));
            }

            _clientId = clientId;
            _tags = tags ?? throw new ArgumentNullException(nameof(tags));
            _dataSource = ds ?? throw new ArgumentNullException(nameof(ds));

            _log = LogManager.GetCurrentClassLogger();
            _httpClient = new HttpClient();
            _rnd = new Random();

            Task.Factory.StartNew(() => InitDatabase());
            _log.Info($"Imgur source started");
        }

        public async Task<string> NextFile()
        {
            var today = DateTime.UtcNow.Date;
            var images = _dataSource.GetImgurImagesForDate(today).ToList();

            while(!_isInitialized)
            {
                await Task.Delay(TimeSpan.FromSeconds(3));
            }

            var totallyRandomImage = images[_rnd.Next(0, images.Count - 1)];

            while (images.Count != 0)
            {
                var index = _rnd.Next(0, images.Count - 1);
                var image = images[index];
                if (!image.Shown)
                {
                    totallyRandomImage = image;
                    break;
                }
            }

            var fullPath = await DownloadFile(totallyRandomImage.Link);
            _dataSource.MarkImgurImageAsShown(totallyRandomImage);
            return fullPath;
        }

        private async Task<string> DownloadFile(string uri)
        {
            string fileName = uri.Substring(uri.LastIndexOf('/') + 1);
            var directory = Directory.GetCurrentDirectory() + "\\imgs\\";
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var fullPath = directory + fileName;

            if (!File.Exists(fullPath))
            {
                byte[] fileBytes = await _httpClient.GetByteArrayAsync(uri);
                File.WriteAllBytes(fullPath, fileBytes);
            }

            return fullPath;
        }

        private async Task InitDatabase()
        {
            while (true)
            {
                var today = DateTime.UtcNow.Date;
                var imagesCount = _dataSource.GetCountImgurImagesForDate(today);

                if (imagesCount < 500)
                {
                    for (int pageNumber = 0; pageNumber < 10; pageNumber++)
                    {
                        string[] images = null;

                        try
                        {
                            images = await GetImgurImages(pageNumber);
                            if (images.Length == 0)
                            {
                                _log.Error($"Fetched 0 images from Imgur for page {pageNumber}");
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            _log.Error(ex, $"Can't get data from Imgur");
                            break;
                        }

                        _dataSource.AddImgurImages(images, today);
                    }
                }

                _isInitialized = true;
                _log.Info($"Imgur source initialized");

                await Task.Delay(TimeSpan.FromMinutes(10));
            }
        }

        private async Task<string[]> GetImgurImages(int pageNumber)
        {
            string GetAddress(int page) => $"https://api.imgur.com/3/gallery/user/time/day/{page}";

            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, GetAddress(pageNumber)))
            {
                request.Headers.Add("Connection", "keep-alive");
                request.Headers.Authorization = new AuthenticationHeaderValue("Client-ID", "766263aaa4c9882");

                using (var resp = await _httpClient.SendAsync(request))
                {
                    var responseContent = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode)
                    {
                        _log.Error($"Can't get data from Imgur: {Environment.NewLine} {responseContent}");
                        return Array.Empty<string>();
                    }

                    var gallery = JsonConvert.DeserializeObject<ImgurGalleryResponse>(responseContent);
                    if (!gallery.success)
                    {
                        _log.Error($"Can't get data from Imgur: {Environment.NewLine} Status: {gallery.status}");
                        return Array.Empty<string>();
                    }

                    var images = gallery.data.Where(x => x.images != null).Select(x => x.images.First().link).ToArray();
                    return images;
                }
            }
        }
    }
}