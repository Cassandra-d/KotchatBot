namespace KotchatBot.Dto
{
    public class CommandDto
    {
        public string Command { get; set; }
        public string CommandArgument { get; set; }
        public string PostNumber { get; set; }

        public override string ToString()
        {
            return $"{PostNumber} >> {Command} {CommandArgument}";
        }
    }
}
