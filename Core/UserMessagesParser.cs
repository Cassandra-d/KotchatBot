using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KotchatBot.Interfaces;
using KotchatBot.Dto;

namespace KotchatBot.Core
{
    public class UserMessagesParser : IWorker
    {
        private readonly string _messagesFeedAddress;
        private readonly IDataStorage _dataStorage;
        private readonly HttpClient _client;
        private readonly TimeSpan _sleepTime;
        private readonly HashSet<string> _processedMessages; // TODO make restrictions on items amount
        private readonly Logger _log;
        private readonly CancellationTokenSource _cts;
        private BlockingCollection<(string id, string body)> _userCommands;

        public UserMessagesParser(string messagesFeedAddress, IDataStorage dataStorage)
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
            _cts = new CancellationTokenSource();
            _userCommands = new BlockingCollection<(string id, string body)>(new ConcurrentQueue<(string id, string body)>());

            Task.Factory.StartNew(() => WokerLoopAsync());
            _log.Info($"Feed parser started");
        }

        public (string id, string body)? GetNextMessage(TimeSpan waitInterval) // TODO make async
        {
            var cts = new CancellationTokenSource();
            cts.CancelAfter(waitInterval);
            var token = cts.Token;

            try
            {
                return _userCommands.Take(token);
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

        public void Stop()
        {
            _cts.Cancel();
            _log.Info("Cancelling");
        }

        private async Task WokerLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
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
                    _userCommands.Add((postNum, m.body));
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

                await Utils.GetResultOrCancelledAsync(async () => await Task.Delay(_sleepTime, _cts.Token));
            }
        }
    }
}