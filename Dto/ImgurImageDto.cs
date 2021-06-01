using System;

namespace KotchatBot.Dto
{
    public class ImgurImageDto
    {
        public int Id { get; set; }
        public string Link { get; set; }
        public DateTime Timestamp { get; set; }
        public string Tag { get; set; }
        public bool Shown { get; set; }
    }
}