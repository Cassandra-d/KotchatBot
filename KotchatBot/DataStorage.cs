using LiteDB;
using System;

namespace KotchatBot
{
    public class DataStorage : IDisposable
    {
        private const string GENERAL_DB_NAME = @"GeneralData.db";
        private readonly LiteDatabase _generalDb; // TODO lazy singleton

        public DataStorage()
        {
            _generalDb = new LiteDatabase(GENERAL_DB_NAME);
            _generalDb.Rebuild();
        }

        private ILiteCollection<PostedResponseDto> GetResponsesCollection() =>
            _generalDb.GetCollection<PostedResponseDto>("postedResponses");

        public void MessageSentTo(string postNumber)
        {
            var col = GetResponsesCollection();
            var obj = new PostedResponseDto { PostNumber = postNumber, Timestamp = DateTime.UtcNow.Ticks };
            col.Insert(obj);
        }

        public string[] GetAllPostsWithResponsesForLastDay()
        {
            var col = GetResponsesCollection();
            var checkpoint = DateTime.UtcNow.AddDays(-1).Ticks;
            var results = col.Query().Where(x => x.Timestamp >= checkpoint).Select(x => x.PostNumber).ToArray();
            return results;
        }

        public void Dispose()
        {
            _generalDb?.Dispose();
        }
    }

    public class PostedResponseDto
    {
        public string PostNumber { get; set; }
        public long Timestamp { get; set; }
    }
}
