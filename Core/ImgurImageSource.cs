using KotchatBot.Dto;
using KotchatBot.Interfaces;
using Newtonsoft.Json;
using NLog;
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace KotchatBot.Core
{
    public class ImgurImageSource : IRandomImageSource
    {
        private readonly string _clientId;
        private readonly IDataStorage _dataStorage;
        private readonly Logger _log;
        private readonly HttpClient _httpClient;
        private readonly Random _rnd;
        private readonly ManualResetEvent _imgurDataInitializationEvent;

        public string Command => ".imgur";

        public ImgurImageSource(string clientId, IDataStorage ds)
        {
            if (string.IsNullOrEmpty(clientId))
            {
                throw new ArgumentException(nameof(clientId));
            }
            if (ds == null)
            {
                throw new ArgumentNullException(nameof(ds));
            }

            _clientId = clientId;
            _dataStorage = ds;
            _log = LogManager.GetCurrentClassLogger();
            _httpClient = new HttpClient();
            _rnd = new Random();
            _imgurDataInitializationEvent = new ManualResetEvent(false);

            Task.Factory.StartNew(() => ImgurFetchImagesLoop());

            _log.Info($"Imgur source started");
        }

        public async Task<string> NextFile(string tag)
        {
            var today = DateTime.UtcNow.Date;
            var images = _dataStorage.GetImgurImagesForDate(today, tag).ToList();

            if (images.Count == 0) // no initialized images yet
            {
                if (string.IsNullOrEmpty(tag)) // .imgur command without argument
                {
                    // let's just wait till initialization is finished
                    _imgurDataInitializationEvent.WaitOne();
                }
                else // command with specific subreddit like '.imgur fanny'
                {
                    await ImgurFetchImagesForTag(tag, today);
                    images = _dataStorage.GetImgurImagesForDate(today, tag).ToList();

                    if (images.Count == 0) // specific tag doesn't exist
                    {
                        // let's return generic images without any tag then
                        images = _dataStorage.GetImgurImagesForDate(today, tag: "").ToList();
                    }
                }
            }

            var totallyRandomImage = images[_rnd.Next(0, images.Count - 1)];

            int attemptsLeft = 600;
            while (attemptsLeft > 0)
            {
                var index = _rnd.Next(0, images.Count - 1);
                var image = images[index];
                if (!image.Shown)
                {
                    totallyRandomImage = image;
                    break;
                }
                attemptsLeft -= 1;
            }
            // if we run out of attempts (all images were already shown, let's return random already shown image)

            var fullPath = await DownloadFile(totallyRandomImage.Link);
            _dataStorage.MarkImgurImageAsShown(totallyRandomImage);
            return fullPath;
        }

        public async Task<string> NextFile()
        {
            return await NextFile("");
        }

        private async Task ImgurFetchImagesForTag(string tag, DateTime today)
        {
            var baseAddress = $"https://api.imgur.com/3/gallery/r/{tag}/time/day/";
            _dataStorage.AddImgurImages(await GetImgurImages(0, baseAddress), today, tag);
            _dataStorage.AddImgurImages(await GetImgurImages(0, baseAddress), today, tag);
        }

        private async Task ImgurFetchImagesLoop()
        {
            while (true) // no graceful cancellation because this logic does not need it
            {
                var today = DateTime.UtcNow.Date;
                var imagesCount = _dataStorage.GetImgurImagesForDate(today, tag: "").Length;

                if (imagesCount < 500)
                {
                    for (int pageNumber = 0; pageNumber < 10; pageNumber++)
                    {
                        string[] images = null;

                        try
                        {
                            var baseAddress = @"https://api.imgur.com/3/gallery/user/time/day/";
                            images = await GetImgurImages(pageNumber, baseAddress);
                            if (images.Length == 0)
                            {
                                _log.Error($"Fetched 0 images from Imgur for page {pageNumber}");
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            _log.Error(ex, $"Can't fetch images from Imgur");
                            break;
                        }

                        _dataStorage.AddImgurImages(images, today, tag: "");
                    }
                }

                _imgurDataInitializationEvent.Set();
                _log.Info($"Imgur source initialized");

                await Task.Delay(TimeSpan.FromMinutes(10));
            }
        }

        private async Task<string[]> GetImgurImages(int pageNumber, string baseAddress)
        {
            string GetAddress(int page) => $"{baseAddress}{page}";

            using (HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, GetAddress(pageNumber)))
            {
                request.Headers.Add("Connection", "keep-alive");
                request.Headers.Authorization = new AuthenticationHeaderValue("Client-ID", _clientId);

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

                    long sixMegabytes = 6291456; // bytes
                    var images = gallery.data
                        .Where(x => x.images != null ||
                                    (x.link?.Contains("jpg") ?? false) ||
                                    (x.link?.Contains("png") ?? false) ||
                                    (x.link?.Contains("mp4") ?? false) ||
                                    (x.link?.Contains("jpeg") ?? false))
                        .Select(x => x.images?.Where(z => z.size <= sixMegabytes).FirstOrDefault()?.link ?? x.link)
                        .Where(y => y != null)
                        .ToArray();
                    return images;
                }
            }
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
    }
}