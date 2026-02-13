namespace API_UP_02.GigaChat_LLM.For_GigaChat.Models
{
    public class Request
    {
        public string model { get; set; }
        public List<Message> messages { get; set; }
        public bool stream { get; set; }
        public int repetition_penalty { get; set; }
        public class Message
        {
            public string role { get; set; }
            public string content { get; set; }
        }
    }
}
