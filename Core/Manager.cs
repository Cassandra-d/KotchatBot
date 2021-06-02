using NLog;
using System;
using System.Threading.Tasks;
using KotchatBot.Interfaces;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace KotchatBot.Core
{
    public class Manager : IWorker
    {
        private const int NEXT_MESSAGE_WAIT_INTERVAL_SECONDS = 1;

        private readonly MessageSender _messageSender;
        private readonly UserMessagesParser _userMessagesParser;
        private readonly IDataStorage _dataStorage;
        private readonly Dictionary<string, IRandomImageSource> _imagesSource;
        private readonly Logger _log;
        private readonly CancellationTokenSource _cts;

        public Manager(MessageSender messageSender, UserMessagesParser userMessagesParser,
            IDataStorage dataStorage, IEnumerable<IRandomImageSource> imagesSource)
        {
            _messageSender = messageSender;
            _userMessagesParser = userMessagesParser;
            _dataStorage = dataStorage;
            _imagesSource = imagesSource.ToDictionary(x => x.Command);
            _log = LogManager.GetCurrentClassLogger();
            _cts = new CancellationTokenSource();

            Task.Factory.StartNew(() => WorkerLoop());

            _log.Info($"Manager started");
        }

        private async Task WorkerLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                var message = _userMessagesParser.GetNextMessage(TimeSpan.FromSeconds(NEXT_MESSAGE_WAIT_INTERVAL_SECONDS));
                if (message == null)
                {
                    continue;
                }

                if (!_imagesSource.TryGetValue(message.Command, out var specificSource))
                {
                    continue;
                }

                try
                {
                    var image = await specificSource.NextFile(message.CommandArgument);
                    var formattedResponse = $">>{message.PostNumber}";

                    // we could add retry policy here, but it doesn't allign with how I see it working
                    _messageSender.Send(formattedResponse, image);
                    _dataStorage.MessageSentTo(message.PostNumber);
                }
                catch (Exception ex)
                {
                    _log.Error(ex);
                }
            }
        }

        public void Stop()
        {
            _cts.Cancel();
            _messageSender.Stop();
            _userMessagesParser.Stop();
            _log.Info("Cancelling");
        }
    }
}