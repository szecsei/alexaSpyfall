﻿using Alexa.NET.LocaleSpeech;
using Alexa.NET.Request;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AlexaSpyfall.Extensions
{
    public static class LanguageExtension
    {
        public static ILocaleSpeech CreateLocale(this SkillRequest skillRequest, DictionaryLocaleSpeechStore store)
        {
            var localeSpeechFactory = new LocaleSpeechFactory(store);
            var locale = localeSpeechFactory.Create(skillRequest);

            return locale;
        }
    }
}
