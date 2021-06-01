using KotchatBot.Dto;
using System;

namespace KotchatBot.Interfaces
{
    public interface IDataStorage : IDisposable
    {
        void AddImgurImages(string[] images, DateTime date, string tag);
        ImgurImageDto[] GetImgurImagesForDate(DateTime today, string tag);

        string[] GetAllPostsWithResponsesForLastDay();
        void MarkImgurImageAsShown(ImgurImageDto image);
        void MessageSentTo(string postNumber);
    }
}