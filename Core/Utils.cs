using System;
using System.Threading.Tasks;

namespace KotchatBot.Core
{
    public static class Utils
    {
        public static bool GetResultOrCancelled<T>(Func<T> action, out T result)
        {
            try
            {
                result = action();
                return true;
            }
            catch (OperationCanceledException)
            {
                result = default(T);
                return false;
            }
        }

        public static async Task<bool> GetResultOrCancelledAsync(Func<Task> action)
        {
            try
            {
                await action();
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }
    }
}
