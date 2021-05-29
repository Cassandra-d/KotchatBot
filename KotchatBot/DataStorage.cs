using LiteDB;
using System;
using System.Linq;

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

        private ILiteCollection<ImgurImageDto> GetImgurImagesCollection() =>
            _generalDb.GetCollection<ImgurImageDto>("imgurImages");

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

        public int GetCountImgurImagesForDate(DateTime today)
        {
            var col = GetImgurImagesCollection();
            var count = col.Query().Where(x => x.Timestamp >= today).Count();
            return count;
        }

        public void AddImgurImages(string[] images, DateTime date)
        {
            var col = GetImgurImagesCollection();
            var objs = images.Select(x => new ImgurImageDto { Link = x, Shown = false, Tag = "", Timestamp = date });
            col.Insert(objs);
        }

        public ImgurImageDto[] GetImgurImagesForDate(DateTime today)
        {
            var col = GetImgurImagesCollection();
            var result = col.Query().Where(x => x.Timestamp >= today);
            return result.ToArray();
        }

        internal void MarkImgurImageAsShown(ImgurImageDto image)
        {
            var col = GetImgurImagesCollection();
            image.Shown = true;
            col.Update(image);
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

    public class ImgurImageDto
    {
        public int Id { get; set; }
        public string Link { get; set; }
        public DateTime Timestamp { get; set; }
        public string Tag { get; set; }
        public bool Shown { get; set; }
    }
}
