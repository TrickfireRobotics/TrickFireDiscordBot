using Notion.Client;
using System.Collections.Generic;

namespace TrickFireDiscordBot.Notion
{
    /// <summary>
    /// Various helpers for the PaginatedList<T> class.
    /// </summary>
    public static class PaginatedListHelper
    {
        /// <summary>
        /// Returns an async enumerable of every item in the paginated list.
        /// </summary>
        /// <typeparam name="T">The generic type of the PaginatedList</typeparam>
        /// <param name="query">A method to query the next page of items given the cursor</param>
        /// <param name="cursor">The cursor to start looping over</param>
        /// <param name="delaySecs">The delay to use between requests</param>
        /// <returns>An async enuerable of every item in the paginated list</returns>
        public static async IAsyncEnumerable<T> GetEnumerable<T>(
            Func<string?, Task<PaginatedList<T>>> query, string? cursor = null, float delaySecs = 0.3f)
        {
            PaginatedList<T> list;
            do
            {
                await Task.Delay((int)(delaySecs * 1000));
                list = await query(cursor);
                cursor = list.NextCursor;

                foreach (T item in list.Results)
                {
                    yield return item;
                }
            }
            while (list.HasMore);
        }
    }
}
