using KotchatBot.Interfaces;
using KotchatBot.Core;
using NLog;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace KotchatBot.Core
{
    public class MessageSender : IWorker
    {
        private readonly Uri _postAddress;
        private readonly string _name;
        private readonly BlockingCollection<(string message, string pictureName)> _messagesQ;
        private readonly Logger _log;
        private readonly HttpClient _client;
        private readonly CookieContainer _cookies;
        private readonly bool _isInitialized = true;
        private readonly CancellationTokenSource _cts;
        private DateTime _lastMessageSent;

        public MessageSender(Uri postAddress, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("message", nameof(name));
            }

            _postAddress = postAddress;
            _name = name;
            var internalCollection = new ConcurrentQueue<(string message, string pictureName)>();
            _messagesQ = new BlockingCollection<(string message, string pictureName)>(internalCollection);
            _lastMessageSent = DateTime.MinValue;
            _cts = new CancellationTokenSource();
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

            if (_isInitialized)
            {
                Task.Factory.StartNew(() => WokerLoopAsync());
            }

            var initStateText = _isInitialized ? "initialized" : "not initialized";
            _log.Info($"Sender started {initStateText}");
        }

        public void Send(string msg, string pictureName = null)
        {
            if (!_isInitialized)
            {
                throw new InvalidOperationException("Sender isn't initialized properly");
            }

            _messagesQ.Add((msg, pictureName));
        }

        public void Stop()
        {
            if (_isInitialized)
            {
                _cts.Cancel();
                _log.Info("Cancelling");
            }
        }

        private async Task WokerLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                Utils.GetResultOrCancelled(() => _messagesQ.Take(_cts.Token), out var message);

                if (_cts.IsCancellationRequested)
                {
                    return;
                }

                var waitTimeMsec =
                    TimeSpan.FromSeconds(3).TotalMilliseconds -
                    (DateTime.UtcNow - _lastMessageSent).TotalMilliseconds;

                if (waitTimeMsec > 0)
                {
                    await Utils.GetResultOrCancelledAsync(async () => await Task.Delay((int) waitTimeMsec, _cts.Token));
                }

                if (_cts.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    await SendInternal(message.message, message.pictureName);
                }
                catch (Exception ex)
                {
                    _log.Error(ex, $"Message: {message}{Environment.NewLine}");
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

                using (HttpResponseMessage response = await _client.SendAsync(request, _cts.Token))
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
            catch (OperationCanceledException) { }
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

            var cookie = sessionCookie.Split(new[] { ";" }, StringSplitOptions.None).First();
            _cookies.SetCookies(new Uri(HostWScheme(_postAddress)), cookie);

            _log.Info($"Session cookie: {cookie}");
            return true;
        }
    }
}