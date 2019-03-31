using System;
using System.Collections.Generic;
using System.Text;

namespace AlexaSpyfall.Models
{
    class Cards
    {
        public string id { get; set; }
        public Dictionary<string, Dictionary<int,string>> symbols { get; set; }
    }
}
