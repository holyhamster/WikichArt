using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Linq;
using Microsoft.Extensions.Configuration;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Net.Http;
using Azure.Data.Tables;
using WikiSentiment;
using Azure.Core.Cryptography;

namespace WikiDBUpdaterHttp
{
    /// <summary>
    /// HTTP triggered Azure Function
    /// Grabs parameter from the url and calls DBUpdater
    /// </summary>
    public class WikiDBUpdaterHttp
    {
        private ConfigurationWrapper config;

        private HashSet<string> allLanguageCodes;

        private HttpClient httpClient;

        private Dictionary<string, HashSet<string>> articleExceptions;

        public WikiDBUpdaterHttp(IConfiguration iConfig)
        {
            config = new ConfigurationWrapper(iConfig);

            var exceptionList = config.GetValue<Dictionary<string, string[]>>
                ("FunctionValues:CountryExceptions");
            articleExceptions = new Dictionary<string, HashSet<string>>();
            foreach (var iKey in exceptionList.Keys)
                articleExceptions[iKey] = exceptionList[iKey].ToHashSet();

            allLanguageCodes = config.GetValue<string[]>("FunctionValues:CountryCodes").ToHashSet();


            httpClient = new HttpClient();

            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                 config.GetValue<string>("WikiKeys:WikiAPIToken"));

            httpClient.DefaultRequestHeaders.Add("Api-User-Agent",
                config.GetValue<string>("WikiKeys:WikiUserContact"));
        }

        /// <summary>
        /// Azure binding that launches the function
        /// </summary>
        /// <param name="req"></param>
        /// <param name="tableClient"></param>
        /// <param name="log"></param>
        /// <returns></returns>
        [FunctionName("WikiDBUpdaterHttp")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", Route = null)] HttpRequest req,
            [Table("datatables"), StorageAccount("AzureWebJobsStorage")] TableClient tableClient, 
            ILogger log)
        {
            //url schema is /WikiDBUpdaterHttp?date=2021-12-31&days=3&discard&language=de,fr
            //everything except date is optional
            string YYYYMMDD = req.Query["date"];
            DateTime startDate;
            if (YYYYMMDD == null)
                return new BadRequestObjectResult("Missing parameter: date");
            if (!DateTime.TryParse(YYYYMMDD, out startDate))
                return new BadRequestObjectResult("Bad starting date: " + YYYYMMDD);

            int daysToGo = 1;
            string daysToGoString = req.Query["days"];
            if (daysToGoString != null &&
                !int.TryParse(daysToGoString, out daysToGo))
                    return new BadRequestObjectResult($"Missing parameter: {nameof(daysToGoString)}");


            HashSet<string> languages = allLanguageCodes;
            string languageStrings = req.Query["languages"];
            if (languageStrings != null &&
                !validateLanguages(languageStrings, out languages))
                return new BadRequestObjectResult("Bad languages parameter: " + languageStrings);


            string discardString = req.Query["discard"];
            bool discardOldData = discardString != null ? true : false;

            IDataBaseClient dbClient = new AzureStorageClient(tableClient);

            await DataBaseBuilder.updateDatabase(startDate, daysToGo, discardOldData, languages, 
                articleExceptions, httpClient, dbClient, log);
            
            return new OkObjectResult("Successfull execution");
        }

        /// <summary>
        /// Validates list of languages in "","","" format
        /// </summary>
        /// <param name="proposedLanguagesString">string in "","","", format</param>
        /// <param name="languageArray">out for an output array</param>
        /// <returns>returns true if string parsed into array</returns>
        bool validateLanguages(string proposedLanguagesString, out HashSet<string> languageArray)
        {
            languageArray = proposedLanguagesString.ToLowerInvariant().Split(',').ToHashSet();
            foreach (var iLanguage in languageArray)
            {
                if (!allLanguageCodes.Contains(iLanguage))
                {
                    return false;
                }
            }
            return true;
        }
    }
}
