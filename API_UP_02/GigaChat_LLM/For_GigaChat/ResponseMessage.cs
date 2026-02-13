namespace API_UP_02.GigaChat_LLM.Model_GigaChat
{
    public class ResponseMessage
    {
        public List<Choice> choices { get; set; }
        public int created { get; set; }
        public string model { get; set; }
        public string @object { get; set; }
        public Usage usage { get; set; }
        public class Usage
        {
            public int complition_token { get; set; }
            public int prompt_tokens { get; set; }
            public int system_tokens { get; set; }
            public int total_tokens { get; set; }
        }
        public class Choice
        {
            public string finish_reason { get; set; }
            public int index { get; set; }
            public GigaChat_LLM.For_GigaChat.Models.Request.Message message { get; set; }
        }
    }
}
