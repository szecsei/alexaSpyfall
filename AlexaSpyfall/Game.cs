using System;
using System.Collections.Generic;
using System.Text;

namespace AlexaSpyfall
{
    class Game
    {
        public string id { get; set; }
        public string Location { get; set; }
        public Dictionary<string,double> Players { get; set; }
        public List<string> QuestionsAsked { get; set; }
    }

}
