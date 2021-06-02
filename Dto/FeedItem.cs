using System;

namespace KotchatBot.Dto
{
    public class FeedItem
    {
        public string _id { get; set; }
        public string count { get; set; }
        public string country_name { get; set; }
        public string country { get; set; }
        public string trip { get; set; }
        public string identifier { get; set; }
        public string name { get; set; }
        public string body { get; set; }
        public string convo { get; set; }
        public string chat { get; set; }
        public DateTime date { get; set; }
        public string thumb { get; set; }
        public int image_height { get; set; }
        public int image_width { get; set; }
        public int image_filesize { get; set; }
        public string image_filename { get; set; }
        public string image { get; set; }
    }

}
