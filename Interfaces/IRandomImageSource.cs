using System.Threading.Tasks;

namespace KotchatBot.Interfaces
{
    public interface IRandomImageSource
    {
        Task<string> NextFile();
        string Command { get; }
    }
}