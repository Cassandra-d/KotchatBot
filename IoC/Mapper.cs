using Autofac;
using KotchatBot.Interfaces;
using System;

namespace IoC
{
    public static class Utils
    {
        public static void RegisterDependencies(Autofac.ContainerBuilder containerBuilder)
        {
            containerBuilder
                .Register<KotchatBot.Core.ImgurImageSource>(c =>
                {
                    var config = c.Resolve<KotchatBot.Configuration.ImgurDataSourceOptions>();
                    var ds = c.Resolve<KotchatBot.Interfaces.IDataStorage>();
                    return new KotchatBot.Core.ImgurImageSource(config.ClientId, ds);
                })
                .As<IRandomImageSource>();

            containerBuilder.Register<KotchatBot.Core.FolderImageSource>(c =>
            {
                var config = c.Resolve<KotchatBot.Configuration.FolderDataSourceOptions>();
                return new KotchatBot.Core.FolderImageSource(config.Path);
            }).As<IRandomImageSource>();

            containerBuilder.Register<KotchatBot.Core.UserMessagesParser>(c => {
                var config = c.Resolve<KotchatBot.Configuration.GeneralOptions>();
                var ds = c.Resolve<KotchatBot.Interfaces.IDataStorage>();
                return new KotchatBot.Core.UserMessagesParser(config.UserMessagesFeedAddress, ds);
            }).SingleInstance();

            containerBuilder.Register<KotchatBot.Core.MessageSender>(c => {
                var config = c.Resolve<KotchatBot.Configuration.GeneralOptions>();
                var uri = new Uri(new Uri(config.HostAddress), config.RelativeAddress);
                return new KotchatBot.Core.MessageSender(uri, config.BotName);
            }).SingleInstance();

            containerBuilder.RegisterType<KotchatBot.DataLayer.DataStorage>().As<IDataStorage>().SingleInstance();
            containerBuilder.RegisterType<KotchatBot.Core.Manager>().SingleInstance();
        }
    }
}
