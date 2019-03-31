using Alexa.NET;
using Alexa.NET.LocaleSpeech;
using Alexa.NET.Request;
using Alexa.NET.Request.Type;
using Alexa.NET.Response;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.Documents.Linq;
using Newtonsoft.Json;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using AlexaSpyfall.Extensions;
using AlexaSpyfall.Models;
using System.Text;

namespace AlexaSpyfall
{
    public static class Skill
    {
        [FunctionName("AlexaSpyfall")]
        public static async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequest req,
        [CosmosDB(databaseName: "spyfalldb", collectionName: "locations", ConnectionStringSetting = "CosmosDBConnection")] DocumentClient locationClient,
        [CosmosDB(databaseName: "spyfalldb", collectionName: "games", ConnectionStringSetting = "CosmosDBConnection")] DocumentClient gameClient,
        ILogger log)
        {
            var json = await req.ReadAsStringAsync();
            var skillRequest = JsonConvert.DeserializeObject<SkillRequest>(json);

            // Verifies that the request is indeed coming from Alexa.
            var isValid = await skillRequest.ValidateRequestAsync(req, log);
            if (!isValid)
            {
                return new BadRequestResult();
            }
            Session session = skillRequest.Session;
            // Setup language resources.
            var store = SetupLanguageResources();
            var locale = skillRequest.CreateLocale(store);

            var request = skillRequest.Request;
            SkillResponse response = null;

            try
            {
                if (request is LaunchRequest launchRequest)
                {
                    log.LogInformation("Session started");

                    //var welcomeMessage = await locale.Get(LanguageKeys.Welcome, null);
                    var welcomeMessage = "Hello";
                    //var welcomeRepromptMessage = await locale.Get(LanguageKeys.WelcomeReprompt, null);
                    var welcomeRepromptMessage = "Hello Again!";
                    response = ResponseBuilder.Ask(welcomeMessage, RepromptBuilder.Create(welcomeRepromptMessage));
                }
                else if (request is IntentRequest intentRequest)
                {
                    // Checks whether to handle system messages defined by Amazon.
                    var systemIntentResponse = await HandleSystemIntentsAsync(intentRequest, locale,locationClient,gameClient,session,log);
                    if (systemIntentResponse.IsHandled)
                    {
                        response = systemIntentResponse.Response;
                    }
                    else
                    {
                        // Processes request according to intentRequest.Intent.Name...
                        var message = await locale.Get(LanguageKeys.Response, null);
                        response = ResponseBuilder.Tell(message);

                        // Note: The ResponseBuilder.Tell method automatically sets the
                        // Response.ShouldEndSession property to true, so the session will be
                        // automatically closed at the end of the response.
                    }
                }
                else if (request is SessionEndedRequest sessionEndedRequest)
                {
                    log.LogInformation("Session ended");
                    response = ResponseBuilder.Empty();
                }
            }
            catch
            {
                var message = await locale.Get(LanguageKeys.Error, null);
                response = ResponseBuilder.Tell(message);
                response.Response.ShouldEndSession = false;
            }

            return new OkObjectResult(response);
        }

        private static async Task<(bool IsHandled, SkillResponse Response)> HandleSystemIntentsAsync(IntentRequest request, 
            ILocaleSpeech locale, 
            DocumentClient locationClient,
            DocumentClient gameClient,
            Session session,
            ILogger log)
        {
            SkillResponse response = null;

            switch (request.Intent.Name)
            {
                case IntentNames.Cancel:
                    {
                        var message = await locale.Get(LanguageKeys.Cancel, null);
                        response = ResponseBuilder.Tell(message);
                        break;
                    }

                case IntentNames.Help:
                    {
                        var message = await locale.Get(LanguageKeys.Help, null);
                        response = ResponseBuilder.Ask(message, RepromptBuilder.Create(message));
                        break;
                    }

                case IntentNames.Stop:
                    {
                        var message = await locale.Get(LanguageKeys.Stop, null);
                        response = ResponseBuilder.Tell(message);
                        break;
                    }
                case IntentNames.StartGame:
                    {
                        var message = await locale.Get(LanguageKeys.StartGame, null);
                        await gameClient.CreateDocumentAsync(UriFactory.CreateDocumentCollectionUri("spyfalldb", "games"), new Game { id = session.SessionId, Players = new Dictionary<string, double>(),
                            QuestionsAsked = new List<string>(), Location = "", Cards = new List<int>() });
                        response = ResponseBuilder.Ask(message, RepromptBuilder.Create(message));
                        break;
                    }
                case IntentNames.AddPlayer:
                    {
                        var collectURI =  UriFactory.CreateDocumentCollectionUri("spyfalldb", "games");
                        int generated = 0;
                        try
                        {
                            Game next = gameClient.CreateDocumentQuery<Game>(collectURI, new FeedOptions { EnableCrossPartitionQuery = true }).Where(p => p.id.Equals(session.SessionId)).AsEnumerable().FirstOrDefault() ;
                            next.Players.Add(request.Intent.Slots["player"].Value, 0.0);
                            Random rnd = new Random();
                            do
                            {
                                generated = rnd.Next(29)+1;
                            } while (next.Cards.Contains(generated));
                            next.Cards.Add(generated);
                            await gameClient.UpsertDocumentAsync(collectURI, next);
                        } catch(Exception e)
                        {
                            log.LogError(e.ToString());
                            throw e;
                        }
                        log.LogInformation("Something very distinct");
                 
                        var message = "I have assigned you card number "+generated;
                        response = ResponseBuilder.Ask(message, RepromptBuilder.Create(message));
                        break;
                    }
                case IntentNames.PlayGame:
                    {
                        Random r = new Random();
                        var collectLocalURI = UriFactory.CreateDocumentCollectionUri("spyfalldb", "locations");
                        var collectURI = UriFactory.CreateDocumentCollectionUri("spyfalldb", "games");
                        try
                        {
                            LocationIndex locIn = locationClient.CreateDocumentQuery<LocationIndex>(collectLocalURI, new FeedOptions { EnableCrossPartitionQuery = true }).Where(p => p.id.Equals("index")).AsEnumerable().FirstOrDefault();
                            Cards cards = locationClient.CreateDocumentQuery<Cards>(collectLocalURI, new FeedOptions { EnableCrossPartitionQuery = true }).Where(p => p.id.Equals("cards")).AsEnumerable().FirstOrDefault();
                            Game game = gameClient.CreateDocumentQuery<Game>(collectURI, new FeedOptions { EnableCrossPartitionQuery = true }).Where(p => p.id.Equals(session.SessionId)).AsEnumerable().FirstOrDefault();
                            log.LogInformation("ourLocal");
                            string ourLocal = locIn.locations[r.Next(locIn.locations.Count)];
                            game.Location = ourLocal;
                            int spy = r.Next(game.Cards.Count);
                            log.LogInformation("spy " + spy);
                            StringBuilder strbuilder = new StringBuilder("For every player, I will read out your card number and the symbol you should look for: ");
                            for(int i =0; i< game.Cards.Count; i++)
                            {
                                var card = game.Cards[i];
                                var symbol = "";
                                if (i == spy)
                                {
  
                                    symbol = cards.symbols["Spy"][card];
                                }else
                                {
                                    symbol = cards.symbols[ourLocal][card];
                                }
                                strbuilder.Append("Card ").Append(card).Append(". ").Append(symbol).Append(". ");
 
                            }
                            log.LogInformation("For loop exit");
                            if (session.Attributes == null)
                                session.Attributes = new Dictionary<string, object>();
                            session.Attributes["questions"] = 0;
                            session.Attributes["askedQuestion"] = 0;
                            session.Attributes["expectedAnswer"] = 0;
                            session.Attributes["playerAsked"] = -1;
                            await gameClient.UpsertDocumentAsync(collectURI, game);
                            var message = strbuilder.ToString();
                            response = ResponseBuilder.Ask(message, RepromptBuilder.Create(message));
                        }
                        catch (Exception e)
                        {
                            log.LogError(e.ToString());
                            throw e;
                        }
                        break;
                    }
                    /*            case IntentNames.StartQuestions:
                                    {
                                        Random r = new Random();
                                        var collectLocalURI = UriFactory.CreateDocumentCollectionUri("spyfalldb", "locations");
                                        var collectURI = UriFactory.CreateDocumentCollectionUri("spyfalldb", "games");
                                        Game game = gameClient.CreateDocumentQuery<Game>(collectURI, new FeedOptions { EnableCrossPartitionQuery = true }).Where(p => p.id.Equals(session.SessionId)).AsEnumerable().FirstOrDefault();
                                        LocationIndex locIn = locationClient.CreateDocumentQuery<LocationIndex>(collectLocalURI, new FeedOptions { EnableCrossPartitionQuery = true }).Where(p => p.id.Equals("index")).AsEnumerable().FirstOrDefault();
                                        Location location = locationClient.CreateDocumentQuery<Location>(collectLocalURI, new FeedOptions { EnableCrossPartitionQuery = true }).Where(p => p.id.Equals(game.Location)).AsEnumerable().FirstOrDefault();
                                        if ((int)session.Attributes["questions"] == 0)
                                        {
                                            var playername = game.Players.First().Key;
                                            var question = location.questions.Keys.ElementAt(r.Next(location.questions.Count));
                                            game.QuestionsAsked.Add(question);
                                            session.Attributes["questions"] = 1;
                                            session.Attributes["askedQuestion"] = 1;
                                            session.Attributes["playerAsked"] = 0;
                                            session.Attributes["expectedAnswer"] = location.questions[question];
                                            await gameClient.UpsertDocumentAsync(collectURI, game);
                                        }
                                        break;
                                    }*/
            }

            return (response != null, response);
        }

        private static DictionaryLocaleSpeechStore SetupLanguageResources()
        {
            // Creates the locale speech store for each supported languages.
            var store = new DictionaryLocaleSpeechStore();

            store.AddLanguage("en", new Dictionary<string, object>
            {
                [LanguageKeys.Welcome] = "Welcome to the skill!",
                [LanguageKeys.WelcomeReprompt] = "You can ask help if you need instructions on how to interact with the skill",
                [LanguageKeys.Response] = "This is just a sample answer",
                [LanguageKeys.Cancel] = "Canceling...",
                [LanguageKeys.Help] = "Help...",
                [LanguageKeys.Stop] = "Bye bye!",
                [LanguageKeys.Error] = "I'm sorry, there was an unexpected error. Please, try again later.",
                [LanguageKeys.StartGame]= "Lets play the game! Join everyone into the game and say lets play the game!",
                [LanguageKeys.AddPlayer]= "New player added. Add another?"
            });

            return store;
        }
    }
}
