using NLog;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Security;
using System.IO;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;

namespace KotchatBot
{
    class Program
    {
        private static IConfiguration _configuration;

        static async Task Main(string[] args)
        {
            using (IHost host = CreateHostBuilder(args).Build())
            {

                var folderDsOptions = new FolderDataSourceOptions();
                var generalOptions = new GeneralOptions();
                _configuration.GetSection(nameof(FolderDataSourceOptions)).Bind(folderDsOptions);
                _configuration.GetSection(nameof(GeneralOptions)).Bind(generalOptions);

                //string hostAddr = @"https://kotchan.fun";
                //string relativeAddress = @"chat/int";
                //var name = "Bot#chip7";
                //var convo = "";
                var sender = new MessageSender(new Uri(new Uri(generalOptions.HostAddress), generalOptions.RelativeAddress), generalOptions.BotName);

                var ds = new DataStorage();

                //var feedAddress = @"https://kotchan.fun/data/int";
                var userMessagesParser = new UserMessagesParser(generalOptions.UserMessagesFeedAddress, ds);

                //var imagesPath = @"\\vboxsrv\int";
                RandomImageSource imgSource = new FolderImageSource(folderDsOptions.Path);

                var mgr = new Manager(sender, userMessagesParser, imgSource);

                await host.RunAsync();
            }
        }

        static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostingContext, configuration) =>
                {
                    configuration.Sources.Clear();

                    IHostEnvironment env = hostingContext.HostingEnvironment;

                    configuration
                        .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                        .AddJsonFile($"appsettings.{env.EnvironmentName}.json", true, true);

                    IConfigurationRoot configurationRoot = configuration.Build();
                    _configuration = configurationRoot;
                });
    }

    public class UserMessagesParser
    {
        private readonly string _messagesFeedAddress;
        private readonly DataStorage _dataStorage;
        private readonly HttpClient _client;
        private readonly TimeSpan _sleepTime;
        private readonly HashSet<string> _processedMessages; // TODO make restrictions on items amount
        private readonly Logger _log;
        private BlockingCollection<(string id, string body)> _messages;

        public UserMessagesParser(string messagesFeedAddress, DataStorage dataStorage)
        {
            if (string.IsNullOrEmpty(messagesFeedAddress))
            {
                throw new ArgumentException("message", nameof(messagesFeedAddress));
            }

            _messagesFeedAddress = messagesFeedAddress;
            _dataStorage = dataStorage;
            _client = new HttpClient();
            _sleepTime = TimeSpan.FromSeconds(3);
            _processedMessages = _dataStorage.GetAllPostsWithResponsesForLastDay().ToHashSet(); // TODO make lazy
            _log = LogManager.GetCurrentClassLogger();
            _messages = new BlockingCollection<(string id, string body)>(new ConcurrentQueue<(string id, string body)>());

            Task.Factory.StartNew(() => HandleMessages());
            _log.Info($"Feed parser started");
        }

        public (string id, string body)? GetNextMessage(TimeSpan waitInterval)
        {
            var cts = new CancellationTokenSource();
            cts.CancelAfter(waitInterval);
            var token = cts.Token;

            try
            {
                return _messages.Take(token);
            }
            catch (OperationCanceledException) // TODO rewrite without exception
            {
                return null;
            }
            finally
            {
                cts.Dispose();
            }
        }

        private async Task HandleMessages()
        {
            while (true)
            {
                var feed = await _client.GetStringAsync(_messagesFeedAddress);
                var messages = JsonConvert.DeserializeObject<FeedItem[]>(feed);
                var newMessages = messages
                    .Where(m =>
                    m.body.ToLower().Contains(".random") &&
                    !_processedMessages.Contains(m.count.ToString())).ToArray();

                foreach (var m in newMessages.OrderBy(x => x.date))
                {
                    var postNum = m.count.ToString();
                    _messages.Add((postNum, m.body));
                    _processedMessages.Add(postNum);

                    try
                    {
                        _dataStorage.MessageSentTo(postNum);
                        _log.Info($"Found: {m.body}");

                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex);
                    }
                }

                await Task.Delay(_sleepTime);
            }
        }
    }

    public class MessageSender
    {
        private readonly Uri _postAddress;
        private readonly string _name;
        private readonly BlockingCollection<string> _messagesQ;
        private DateTime _lastMessageSent;
        private readonly Logger _log;
        private readonly HttpClient _client;
        private readonly CookieContainer _cookies;
        private const string DATA_SEPARATOR = "______";
        private readonly bool _isInitialized = true;

        public MessageSender(Uri postAddress, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("message", nameof(name));
            }

            _postAddress = postAddress;
            _name = name;
            _messagesQ = new BlockingCollection<string>(new ConcurrentQueue<string>());
            _lastMessageSent = DateTime.MinValue;
            _log = LogManager.GetCurrentClassLogger();

            _cookies = new CookieContainer();
            var httpClientHandler = new HttpClientHandler
            {
                // uncomment for debug
                //Proxy = new WebProxy("http://localhost:8888", false),
                //UseProxy = true,
                //ServerCertificateCustomValidationCallback = (HttpRequestMessage a, X509Certificate2 b, X509Chain c, SslPolicyErrors d) => { return true; };
            };
            httpClientHandler.CookieContainer = _cookies;
            _client = new HttpClient(httpClientHandler);
            _isInitialized &= InitCookies();

            Task.Factory.StartNew(() => SendingLoopAsync());

            var initStateText = _isInitialized ? "initialized" : "not initialized";
            _log.Info($"Sender started {initStateText}");
        }

        private string HostWScheme(Uri uri) => $"{uri.Scheme}://{uri.Host}";

        private bool InitCookies()
        {
            string sessionCookie = null;
            string responseText = null;
            var req = CreateRequest(HttpMethod.Get);

            try
            {
                var resp = _client.SendAsync(req).ConfigureAwait(false).GetAwaiter().GetResult();
                resp.EnsureSuccessStatusCode();
                sessionCookie = resp.Headers.GetValues("Set-Cookie").FirstOrDefault();
                responseText = resp.Content.ReadAsStringAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                // don't throw exceptions in constructor for safety
                _log.Error(ex, "Getting cookies");
                return false;
            }
            finally
            {
                req.Dispose();
            }

            if (string.IsNullOrEmpty(sessionCookie))
            {
                _log.Error($"Can't get session cookie:{Environment.NewLine}{responseText}");
                return false;
            }

            var cookie = sessionCookie.Split(";").First();
            _cookies.SetCookies(new Uri(HostWScheme(_postAddress)), cookie);

            _log.Info($"Session cookie: {cookie}");
            return true;
        }

        public void Send(string msg, string pictureName = null)
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("Sender isn't initialized properly");
            }

            var data = $"{msg}{DATA_SEPARATOR}{pictureName}";
            _messagesQ.Add(data);
        }

        private async Task SendingLoopAsync()
        {
            while (true)
            {
                var msg = _messagesQ.Take();
                var waitTimeMsec =
                    TimeSpan.FromSeconds(3).TotalMilliseconds -
                    (DateTime.UtcNow - _lastMessageSent).TotalMilliseconds;

                if (waitTimeMsec > 0)
                {
                    System.Threading.Thread.Sleep((int) waitTimeMsec);
                }

                try
                {
                    var arr = msg.Split(DATA_SEPARATOR);
                    await SendInternal(arr[0], arr[1]);
                }
                catch (Exception ex)
                {
                    _log.Error(ex, $"Message: {msg}{Environment.NewLine}");
                }
                _lastMessageSent = DateTime.UtcNow;
            }
        }

        private async Task SendInternal(string msg, string imagePath)
        {
            HttpRequestMessage request = CreateRequest(HttpMethod.Post);

            ByteArrayContent content = null;
            if (!string.IsNullOrEmpty(imagePath))
            {
                var filename = System.IO.Path.GetFileName(imagePath);
                var bytes = System.IO.File.ReadAllBytes(imagePath);
                content = new ByteArrayContent(bytes);
                content.Headers.ContentType = new MediaTypeHeaderValue(MimeMapping.MimeUtility.GetMimeMapping(imagePath));
                content.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data")
                {
                    Name = "\"image\"",
                    FileName = $"\"{filename}\"",
                };
            }

            var form = new MultipartFormDataContent();
            form.Add(new StringContent(_name), "\"name\"");
            form.Add(new StringContent(""), "\"convo\"");
            form.Add(new StringContent(msg), "\"body\"");
            if (content != null)
            {
                form.Add(content);
            }
            request.Content = form;

            try
            {
                using (HttpResponseMessage response = await _client.SendAsync(request))
                {
                    response.EnsureSuccessStatusCode();
                    string response_content = await GetResponseContent(response);

                    if (response.IsSuccessStatusCode)
                    {
                        _log.Info($"Message sent to {msg}:{Environment.NewLine}{imagePath}{Environment.NewLine}{response_content}");
                    }
                    else
                    {
                        _log.Error(response_content);
                    }
                }
            }
            catch (Exception e)
            {
                _log.Error(e);
            }
            finally
            {
                // reducing nesting, that's why without 'using'
                content?.Dispose();
                form?.Dispose();
                request?.Dispose();
            }
        }

        private async Task<string> GetResponseContent(HttpResponseMessage response)
        {
            var encoding = response.Content.Headers.ContentEncoding.Select(x => x.ToLower()).ToHashSet();
            var responseStream = await response.Content.ReadAsStreamAsync();
            Stream compressorStream = null;

            if (encoding.Contains("gzip"))
            {
                compressorStream = new System.IO.Compression.GZipStream(responseStream, System.IO.Compression.CompressionMode.Decompress);
            }
            else if (encoding.Contains("deflate"))
            {
                compressorStream = new System.IO.Compression.DeflateStream(responseStream, System.IO.Compression.CompressionMode.Decompress);
            }

            if (compressorStream != null)
            {
                using (var sr = new StreamReader(compressorStream))
                using (compressorStream)
                {
                    return await sr.ReadToEndAsync();
                }
            }

            using (var sr = new StreamReader(responseStream))
            {
                // I wonder what will happen with responseStream in case of exception?
                return await sr.ReadToEndAsync();
            }
        }

        private HttpRequestMessage CreateRequest(HttpMethod method)
        {
            HttpRequestMessage request = new HttpRequestMessage(method, _postAddress);
            request.Headers.Add("Connection", "keep-alive");
            request.Headers.Add("Pragma", "no-cache");
            request.Headers.Add("Cache-Control", "no-cache");
            request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
            request.Headers.Add("Accept-Encoding", "gzip, deflate, br");
            request.Headers.Add("DNT", "1");
            request.Headers.Add("X-Requested-With", "XMLHttpRequest");
            request.Headers.Add("Origin", HostWScheme(_postAddress));
            request.Headers.Add("Sec-Fetch-Mode", "cors");
            request.Headers.Add("Sec-Fetch-Dest", "empty");
            request.Headers.Add("Sec-Fetch-Site", "same-origin");
            request.Headers.Add("Accept-Language", "en-US,en;q=0.9");
            request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 6.1) AppleWebKit/537.36 (LOL, Kek, Cheburek)");
            return request;
        }
    }

    public interface RandomImageSource
    {
        string NextFile();
    }

    public class FolderImageSource : RandomImageSource
    {
        private string[] _allFiles;
        private HashSet<int> _usedFiles;
        private Random _rnd;
        private readonly Logger _log;

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

        public string NextFile()
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
            return NextFile();
        }
    }

    public class Manager
    {
        private readonly MessageSender _messageSender;
        private readonly UserMessagesParser _userMessagesParser;
        private readonly RandomImageSource _imagesSource;
        private readonly Logger _log;

        public Manager(MessageSender messageSender, UserMessagesParser userMessagesParser, RandomImageSource imagesSource)
        {
            _messageSender = messageSender;
            _userMessagesParser = userMessagesParser;
            _imagesSource = imagesSource;
            Task.Factory.StartNew(() => MainLoop());

            _log = LogManager.GetCurrentClassLogger();
            _log.Info($"Manager started");
        }


        private void MainLoop()
        {
            while (true)
            {
                var message = _userMessagesParser.GetNextMessage(TimeSpan.FromSeconds(3));
                if (message != null)
                {
                    var image = _imagesSource.NextFile();
                    _messageSender.Send($">>{message.Value.id}", image);
                }
            }
        }
    }
}
