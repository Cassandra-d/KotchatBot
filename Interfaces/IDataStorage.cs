using KotchatBot.Dto;
using System;

namespace KotchatBot.Interfaces
{
    public interface IDataStorage : IDisposable
    {
        void AddImgurImages(string[] images, DateTime date, string tag);
        ImgurImageDto[] GetImgurImagesForDate(DateTime today, string tag);
        void MarkImgurImageAsShown(ImgurImageDto image);

        string[] GetAllPostsWithResponsesForLastDay();
        void MessageSentTo(string postNumber);
    }
}