﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

using static WikiScraper.WikiData;
using System.Collections;

using Azure.Data.Tables;
using Azure;
using System.Text.Encodings.Web;
using System.Reflection;

namespace WikiScraper
{
    //entry for daily data about wikipedia project with an article split
    public static class WikiData
    {
        public class MonthlyCollection
        {
            public Dictionary<string, DailyCollection> dailyData { get; set; }
            public string date { get; set; }

            public MonthlyCollection(string _date)
            {
                date = _date;
                dailyData = new Dictionary<string, DailyCollection>();
            }
            public string ToJSON()
            {
                return JsonSerializer.Serialize(this, new JsonSerializerOptions()
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                    WriteIndented = false
                });
            }
        }

        //collection of data for an entire day
        public class DailyCollection
        {
            public Dictionary<string, DailyData> wikiDataList { get; set; }

            public List<string> featuredList { get; set; }

            public async static Task<DailyCollection> BuildCollection(
               DateTime _date, string[] _countryCodes, Dictionary<string, List<string>> _exceptions, HttpClient _client)
            {
                var resultCollection = new DailyCollection()
                {
                    wikiDataList = new Dictionary<string, DailyData>(),
                    featuredList = new List<string>(),
                };

                var dailyData = await Task.WhenAll(_countryCodes.Select(
                    iCountry => DailyData.BuildDailyData(
                        _client, _date, iCountry, _exceptions[iCountry].ToArray(), 3)));

                dailyData.ToList().ForEach(x => resultCollection.wikiDataList[x.countrycode] = x);

                int topAmount = 3;

                var topArticles = new List<(Article article, string code)>();

                //collect all top articles from the collections with their countries
                resultCollection.wikiDataList.Values.ToList().ForEach(
                    iDaily => topArticles.Add((iDaily.articles[0], iDaily.countrycode)));


                //order articles by popularity, get top entries
                topArticles = topArticles.OrderBy(articleTuple => articleTuple.article.prc).Reverse().ToList().
                    GetRange(0, Math.Min(topAmount, topArticles.Count));

                //collect the codes from the top entries
                topArticles.ForEach(articleTuple => resultCollection.featuredList.Add(articleTuple.code));


                return resultCollection;

            }

        }

        //a collection of processed data about one wiki for a single day
        public class DailyData
        {
            public string countrycode { get; set; }

            public int totalviews { get; set; }

            public List<Article> articles { get; set; }

            /// <summary>
            /// build data collection about a single language wiki project
            /// </summary>
            /// <param name="_countryCode"></param>
            /// <param name="_exceptions"></param>
            /// <param name="_totalviews"></param>
            /// <param name="_articles"></param>
            /// <param name="_keepMax"></param>
            /// <returns></returns>
            public static async Task<DailyData> BuildDailyData(
                HttpClient _client, DateTime _date, 
                string _countryCode, string[] _exceptions,
                int _keepMax)
            {
                try
                {
                    var debug = await _client.GetStringAsync(
                                "https://wikimedia.org/api/rest_v1/metrics/pageviews/" +
                                $"aggregate/{_countryCode}.wikipedia.org/all-access/user/daily/" +
                                $"{_date.Year}{_date.Month:D2}{_date.Day:D2}/{_date.Year}{_date.Month:D2}45");
                }
                catch (HttpRequestException e)
                {
                    var sdf = e.ToString();
                }
                int debug2 = 13;
                int totalviews;
                {
                    var viewsString = await _client.GetStringAsync(
                            "https://wikimedia.org/api/rest_v1/metrics/pageviews/" +
                            $"aggregate/{_countryCode}.wikipedia.org/all-access/user/daily/" +
                            $"{_date.Year}{_date.Month:D2}{_date.Day:D2}/{_date.Year}{_date.Month:D2}{_date.Day:D2}");

                    totalviews = (int)JsonNode.Parse(viewsString).AsObject()["items"][0]["views"];
                }

                JsonArray articles;
                {
                    var articlesString = await _client.GetStringAsync(
                           "https://wikimedia.org/api/rest_v1/metrics/pageviews/" +
                           $"top/{_countryCode}.wikipedia.org/all-access/{_date.Year}/{_date.Month:D2}/{_date.Day:D2}");
                    articles = JsonNode.Parse(articlesString).AsObject()["items"][0]["articles"].AsArray();
                }

                var resultData = new DailyData()
                {
                    countrycode = _countryCode,
                    totalviews = totalviews,
                    articles = new List<Article>()
                };

                var articleArray = articles.AsArray();

                int i = 0;
                while (resultData.articles.Count < _keepMax && articleArray.Count > i)
                {
                    var node = articleArray[i];

                    //if the article name is not on the exceptions list
                    if (!_exceptions.Contains((string)node["article"].AsValue()))
                    {

                        var article = new Article()
                        {
                            ttl = (string)node["article"].AsValue(),
                            vws = (int)node["views"].AsValue(),
                            lngl = new Dictionary<string, string>(),
                            //cde = _countryCode
                        };
                                            
                        //article.link = $"https://{_countryCode}.wikipedia.org/wiki/{article.ttl}";
                        article.prc = 100f * article.vws / resultData.totalviews;

                        var langlinkNode = await _client.GetStringAsync(
                            $"https://{_countryCode}.wikipedia.org/w/api.php?action=query&titles=" +
                            $"{article.ttl}&prop=langlinks&format=json&lllang=en");
                        var langlinkObject = JsonNode.Parse(langlinkNode).AsObject()["query"]["pages"];

                        var articleID = JsonSerializer.Deserialize<Dictionary<string, JsonNode>>(langlinkObject).Keys.First();
                        
                        if (langlinkObject[articleID].AsObject().ContainsKey("langlinks"))
                        {
                            article.lngl["en"] = (string)langlinkObject[articleID]["langlinks"][0]["*"];
                        }

                        resultData.articles.Add(article);
                    }
                    
                    i++;
                }

                return resultData;
            }
        }

        public class Article
        {
            //public string cde { get; set; }

            public string ttl { get; set; }

            //public string link { get; set; }

            public int vws { get; set; }

            public float prc { get; set; }

            public Dictionary<string, string> lngl { get; set; }
        }
    }
}
