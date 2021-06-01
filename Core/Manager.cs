using NLog;
using System;
using System.Threading.Tasks;
using KotchatBot.Interfaces;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace KotchatBot.Core
{
    public class Manager
    {
        private readonly MessageSender _messageSender;
        private readonly UserMessagesParser _userMessagesParser;
        private readonly IRandomImageSource[] _imagesSource;
        private readonly Logger _log;

        public Manager(MessageSender messageSender, UserMessagesParser userMessagesParser, IEnumerable<IRandomImageSource> imagesSource)
        {
            _messageSender = messageSender;
            _userMessagesParser = userMessagesParser;
            _imagesSource = imagesSource.ToArray();
            Task.Factory.StartNew(() => MainLoop());

            _log = LogManager.GetCurrentClassLogger();
            _log.Info($"Manager started");
        }

        private async Task MainLoop()
        {
            while (true)
            {
                var message = _userMessagesParser.GetNextMessage(TimeSpan.FromSeconds(3));
                if (message != null)
                {
                    try
                    {
                        var image = await _imagesSource[0].NextFile();
                        _messageSender.Send($">>{message.Value.id}", image);
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex);
                    }
                }
            }
        }

        public async Task Stop(CancellationToken token)
        {
            throw new NotImplementedException();
        }
    }
}