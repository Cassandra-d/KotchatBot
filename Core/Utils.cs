using System;
using System.Threading.Tasks;

namespace KotchatBot.Core
{
    public static class Utils
    {
        /// <summary>
        /// Gets result and returns true or catches OperationCanceledException and returns false
        /// </summary>
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

        /// <summary>
        /// Returns true if action performed without throwind OperationCanceledException, otherwise returns false
        /// </summary>
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
