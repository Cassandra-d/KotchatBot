using KotchatBot.Dto;
using KotchatBot.Interfaces;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace KotchatBot.Core
{
    public class UserMessagesParser : IWorker
    {
        private const int USER_INPUT_WAIT_INTERVAL_SECONDS = 3;

        private readonly string _messagesFeedAddress;
        private readonly IDataStorage _dataStorage;
        private readonly HttpClient _client;
        private readonly Logger _log;
        private readonly CancellationTokenSource _cts;
        private readonly BlockingCollection<CommandDto> _usersCommands;

        private readonly Lazy<HashSet<string>> _processedMessages;
        private HashSet<string> ProcessedMessages => _processedMessages.Value;

        public UserMessagesParser(string messagesFeedAddress, IDataStorage dataStorage)
        {
            if (string.IsNullOrEmpty(messagesFeedAddress))
            {
                throw new ArgumentException("message", nameof(messagesFeedAddress));
            }

            _log = LogManager.GetCurrentClassLogger();
            _messagesFeedAddress = messagesFeedAddress;
            _dataStorage = dataStorage;
            _client = new HttpClient();
            _cts = new CancellationTokenSource();

            _processedMessages = new Lazy<HashSet<string>>(
                () => _dataStorage.GetAllPostsWithResponsesForLastDay().ToHashSet(), isThreadSafe: true);
            _usersCommands = new BlockingCollection<CommandDto>(new ConcurrentQueue<CommandDto>());

            Task.Factory.StartNew(() => WokerLoopAsync());
            _log.Info($"Feed parser started");
        }

        public CommandDto GetNextMessage(TimeSpan waitInterval)
        {
            using (var cts = new CancellationTokenSource())
            {
                cts.CancelAfter(waitInterval);

                return Utils.GetResultOrCancelled(() => _usersCommands.Take(cts.Token), out var command)
                    ? command
                    : null;
            }
        }

        public void Stop()
        {
            _cts.Cancel();
            _log.Info("Cancelling");
        }

        private async Task WokerLoopAsync()
        {
            var userCommandPattern = @"\.[a-z]+ *[^.]\w+";
            // should match:
            //.command parameter but not the rest
            //Should not match at all.
            //.command .differetCommand but not the rest
            //Should not match.Either
            var regex = new Regex(userCommandPattern);

            while (!_cts.IsCancellationRequested)
            {
                var feed = await _client.GetStringAsync(_messagesFeedAddress);
                var newMessages = JsonConvert.DeserializeObject<FeedItem[]>(feed)
                    // if bot restarted, filter out old messages
                    .Where(m => !ProcessedMessages.Contains(m.count)).ToArray();

                foreach (var message in newMessages.OrderBy(x => x.date))
                {
                    var matches = regex.Matches(message.body);
                    var postNumber = message.count;

                    foreach (Match match in matches)
                    {
                        CommandDto commandDto = ExtractCommand(postNumber, match);

                        _usersCommands.Add(commandDto);
                        ProcessedMessages.Add(postNumber);

                        _log.Info($"Found: {commandDto}");
                    }
                }

                await Utils.GetResultOrCancelledAsync(async () => await Task.Delay(USER_INPUT_WAIT_INTERVAL_SECONDS, _cts.Token));
            }
        }

        private static CommandDto ExtractCommand(string postNumber, Match m)
        {
            var data = m.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return new CommandDto()
            {
                Command = data[0],
                CommandArgument = data.Length > 1 ? data[1] : "",
                PostNumber = postNumber,
            };
        }
    }
}