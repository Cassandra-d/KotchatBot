using System.Threading.Tasks;

namespace KotchatBot.Interfaces
{
    public interface IRandomImageSource
    {
        Task<string> NextFile();
        Task<string> NextFile(string parameter);
        string Command { get; }
    }
}