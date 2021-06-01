using KotchatBot.Dto;
using System;

namespace KotchatBot.Interfaces
{
    public interface IDataStorage : IDisposable
    {
        void AddImgurImages(string[] images, DateTime date);
        string[] GetAllPostsWithResponsesForLastDay();
        int GetCountImgurImagesForDate(DateTime today);
        ImgurImageDto[] GetImgurImagesForDate(DateTime today);
        void MarkImgurImageAsShown(ImgurImageDto image);
        void MessageSentTo(string postNumber);
    }
}