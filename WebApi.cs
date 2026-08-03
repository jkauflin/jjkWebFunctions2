/*==============================================================================
(C) Copyright 2024 John J Kauflin, All rights reserved.
--------------------------------------------------------------------------------
DESCRIPTION:  Azure Function for SWA 
--------------------------------------------------------------------------------
Modification History
2024-06-30 JJK  Initial version (moving logic from PHP to here to update data
                in MediaInfo entities in Cosmos DB NoSQL
2024-07-28 JJK  Resolved JSON parse and DEBUG issues and got the update working
2024-08-10 JJK  Added function for getting People list
2024-11-13 JJK  Converted functions to run as dotnet-isolated in .net8.0, 
                logger (connected to App Insights), and added configuration 
                to get environment variables for the Cosmos DB connection str
                Modified to check user role from function context for auth
2025-05-23 JJK  Added functions for GenvMonitor
2025-07-05 JJK  Added UpdateGenvConfig (first time I used Co-Pilot AI agent 
                to help with editing code in VS Code)
2025-12-12 JJK  Added delete functionality to UpdateMediaInfo function
2026-07-25 JJK  Removed AspNetCore (and Mvc for IActionResult) references and
                converted all functions to return HttpResponseData instead of 
                IActionResult for dotnet-isolated .net10
2026-07-28 JJK  Modified to use Newtonsoft.Json.Serialization with camelCase 
                for JSON serialization to match the previous PHP API output
                (and have the first letter of the JSON property names be lower case)
2026-08-03 JJK  Modified the AuthorizationCheck class to use JWT token from 
                Authorization header instead of x-ms-client-principal header 
                (as part of migrating Azure Function to .NET 10 isolated worker model).  
                Authorization check is now done by validating the JWT token 
                and checking for the required role in the "roles" claim 
                (on the Azure Entra ID).  The API Function is a registered 
                application in Azure Entra ID and the roles are defined in the 
                app registration.  
================================================================================*/
using System.Net;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;  // Needed for MultipartReader
using Microsoft.Net.Http.Headers;

using jjkWebFunctions2.Model;

namespace jjkWebFunctions2
{
    public class WebApi
    {
        private readonly ILogger<WebApi> log;
        private readonly IConfiguration config;
        private readonly string? apiCosmosDbConnStr;
        private readonly AuthorizationCheck authCheck;
        private readonly string userAdminRole;
        private readonly DbCommon dbCommon;

        private static List<DatePattern> dpList = new List<DatePattern>();
        private static DateTime minDateTime = new DateTime(1800, 1, 1);
        public class DatePattern
        {
            public Regex regex;
            public string dateParseFormat;
            public DatePattern(Regex regex, string dateParseFormat)
            {
                this.regex = regex;
                this.dateParseFormat = dateParseFormat;
            }   
        }

        public WebApi(ILogger<WebApi> logger, IConfiguration configuration)
        {
            log = logger;
            config = configuration;
            apiCosmosDbConnStr = config["API_COSMOS_DB_CONN_STR"];
            authCheck = new AuthorizationCheck(log, configuration);
            userAdminRole = "jjkadmin";   // add to config ???
            dbCommon = new DbCommon(log, config);
        }

        private void loadDatePatterns()
        {
            if (dpList.Count > 0)
            {
                return;
            }

            // Load the patterns to use for RegEx and DateTime Parse
            DatePattern datePattern;

            datePattern = new DatePattern(
                new Regex(@"(19|20)\d{2}-((0[1-9])|(1[012]))_(19|20)\d{2}((0[1-9])|(1[012]))((0[1-9]|[12]\d)|3[01])"),
                "yyyy-MM_yyyyMMdd");
            dpList.Add(datePattern);

            datePattern = new DatePattern(
                new Regex(@"IMG_(19|20)\d{2}((0[1-9])|(1[012]))((0[1-9]|[12]\d)|3[01])"),
                "IMG_yyyyMMdd");
            dpList.Add(datePattern);

            datePattern = new DatePattern(
                new Regex(@"(19|20)\d{2}((0[1-9])|(1[012]))((0[1-9]|[12]\d)|3[01])_\d{9}_iOS"),
                "yyyyMMdd_iOS");
            dpList.Add(datePattern);

            datePattern = new DatePattern(
                new Regex(@"(19|20)\d{2}((0[1-9])|(1[012]))((0[1-9]|[12]\d)|3[01])"),
                "yyyyMMdd");
            dpList.Add(datePattern);
            // \d{4} to (19|20)\d{2}
            //+		fi	{D:\Photos\1 John J Kauflin\2016-to-2022\2018\01 Winter\FB_IMG_1520381172965.jpg}	System.IO.FileInfo

            datePattern = new DatePattern(
                new Regex(@"(19|20)\d{2}-((0[1-9])|(1[012]))-((0[1-9]|[12]\d)|3[01])"),
                "yyyy-MM-dd");
            dpList.Add(datePattern);

            datePattern = new DatePattern(
                new Regex(@"(19|20)\d{2}_((0[1-9])|(1[012]))_((0[1-9]|[12]\d)|3[01])"),
                "yyyy_MM_dd");
            dpList.Add(datePattern);

            datePattern = new DatePattern(
                new Regex(@"(19|20)\d{2}-((0[1-9])|(1[012]))"),
                "yyyy-MM");
            dpList.Add(datePattern);

            datePattern = new DatePattern(
                new Regex(@"(19|20)\d{2}_((0[1-9])|(1[012]))"),
                "yyyy_MM");
            dpList.Add(datePattern);

            datePattern = new DatePattern(
                new Regex(@"(19|20)\d{2}((0[1-9])|(1[012]))"),
                "yyyyMM");
            dpList.Add(datePattern);

            datePattern = new DatePattern(
                new Regex(@"\\(19|20)\d{2}(\-|\ )"),
                "yyyy");
            dpList.Add(datePattern);

            datePattern = new DatePattern(
                new Regex(@"(\(|\\)(19|20)\d{2}(\)|\\)"),
                "yyyy");
            dpList.Add(datePattern);

            datePattern = new DatePattern(
                new Regex(@"(19|20)\d{2} "),
                "yyyy ");
            dpList.Add(datePattern);

            datePattern = new DatePattern(
                new Regex(@" (19|20)\d{2}"),
                " yyyy");
            dpList.Add(datePattern);
        }

        private DateTime getDateFromFilename(string fileName)
        {
            DateTime outDateTime = new DateTime(9999, 1, 1);
            string dateFormat;
            string dateStr;

            if (fileName.Contains("FB_IMG_"))
            {
                return outDateTime;
            }

            MatchCollection matches;
            bool found = false;
            int index = 0;
            // Loop through the defined RegEx patterns for date, find matches in the filename, and parse to get DateTime

            if (dpList is null)
            {
                return outDateTime;
            }

            while (index < dpList.Count && !found)
            {
                matches = dpList[index].regex.Matches(fileName);
                if (matches.Count > 0)
                {
                    found = true;
                    // If there are multiple matches, just take the last one
                    dateStr = matches[matches.Count - 1].Value;
                    dateFormat = dpList[index].dateParseFormat ?? "";

                    // For this combined case, get the year-month from the start
                    if (dateFormat.Equals("yyyy-MM_yyyyMMdd"))
                    {
                        dateStr = dateStr.Substring(0, 7);
                        dateFormat = "yyyy-MM";
                    }

                    // Majority case - backup from iPhone iOS photos
                    if (dateFormat.Equals("yyyyMMdd_iOS"))
                    {
                        /*
                        20241017_090331090_iOS
                        yyyyMMdd_HHmmssfff_iOS
                        20260421_136707410_iOS.jpg
                                   XX - need to validate all of the date and time elements and adjust them to the correct ranges for month, day, hour, minute, second, and milliseconds (if needed) to get a successful parse to DateTime - otherwise it will fail to parse and return the default 9999-01-01 date
                        */
                        // 2024-10-28 JJK - Add minutes and seconds to the iOS parse (based on how the file name is created on download)
                        if (dateStr.Length >= 18)
                        {
                            dateStr = dateStr.Substring(0, 18);
                            dateFormat = "yyyyMMdd_HHmmssfff";
                        }
                        else if (dateStr.Length >= 15)
                        {
                            dateStr = dateStr.Substring(0, 15);
                            dateFormat = "yyyyMMdd_HHmmss";
                        }
                        else
                        {
                            dateStr = dateStr.Substring(0, 8);
                            dateFormat = "yyyyMMdd";
                        }

                        // Validate and adjust date/time components to valid ranges
                        if (dateFormat.Contains("_"))
                        {
                            var parts = dateStr.Split('_');
                            string datePart = parts[0];
                            string timePart = parts[1];
                            int year = int.Parse(datePart.Substring(0, 4));
                            int month = int.Parse(datePart.Substring(4, 2));
                            int day = int.Parse(datePart.Substring(6, 2));
                            int hour = int.Parse(timePart.Substring(0, 2));
                            int minute = int.Parse(timePart.Substring(2, 2));
                            int second = int.Parse(timePart.Substring(4, 2));
                            int millisecond = dateFormat == "yyyyMMdd_HHmmssfff" ? int.Parse(timePart.Substring(6, 3)) : 0;

                            // Adjust to valid ranges
                            year = Math.Clamp(year, 1900, 2100);
                            month = Math.Clamp(month, 1, 12);
                            day = Math.Clamp(day, 1, DateTime.DaysInMonth(year, month));
                            hour = Math.Clamp(hour, 0, 23);
                            minute = Math.Clamp(minute, 0, 59);
                            second = Math.Clamp(second, 0, 59);
                            millisecond = Math.Clamp(millisecond, 0, 999);

                            // Reconstruct the string
                            datePart = $"{year:D4}{month:D2}{day:D2}";
                            timePart = $"{hour:D2}{minute:D2}{second:D2}";
                            if (dateFormat == "yyyyMMdd_HHmmssfff")
                                timePart += $"{millisecond:D3}";
                            dateStr = $"{datePart}_{timePart}";
                        }
                        else
                        {
                            // Date only validation
                            int year = int.Parse(dateStr.Substring(0, 4));
                            int month = int.Parse(dateStr.Substring(4, 2));
                            int day = int.Parse(dateStr.Substring(6, 2));
                            year = Math.Clamp(year, 1900, 2100);
                            month = Math.Clamp(month, 1, 12);
                            day = Math.Clamp(day, 1, DateTime.DaysInMonth(year, month));
                            dateStr = $"{year:D4}{month:D2}{day:D2}";
                        }
                    }

                    if (dateFormat.Equals("IMG_yyyyMMdd"))
                    {
                        dateStr = dateStr.Substring(4, 8);
                        dateFormat = "yyyyMMdd";
                    }

                    if (dateFormat.Equals("yyyy"))
                    {
                        // Strip off the beginning and ending characters ("\" or "(") form the year match
                        dateStr = dateStr.Substring(1, 4);

                        // Check for a season tag and add a month to the year
                        if (fileName.Contains(" Winter"))
                        {
                            dateFormat = "yyyy-MM";
                            if (fileName.Contains("01 Winter"))
                            {
                                dateStr = dateStr + "-01";
                            }
                            else
                            {
                                dateStr = dateStr + "-11";
                            }
                        }
                        else if (fileName.Contains(" Spring"))
                        {
                            dateFormat = "yyyy-MM";
                            dateStr = dateStr + "-04";
                        }
                        else if (fileName.Contains(" Summer"))
                        {
                            dateFormat = "yyyy-MM";
                            dateStr = dateStr + "-07";
                        }
                        else if (fileName.Contains(" Fall"))
                        {
                            dateFormat = "yyyy-MM";
                            dateStr = dateStr + "-09";
                        }
                    }

                    if (dateFormat.Equals("yyyy "))
                    {
                        // Strip off the beginning and ending characters ("\" or "(") form the year match
                        dateStr = dateStr.Substring(0, 4);
                        dateFormat = "yyyy";
                    }
                    if (dateFormat.Equals(" yyyy"))
                    {
                        // Strip off the beginning and ending characters ("\" or "(") form the year match
                        dateStr = dateStr.Substring(1, 4);
                        dateFormat = "yyyy";
                    }

                    //if (DateTime.TryParseExact(dateStr, dateFormat, null, System.Globalization.DateTimeStyles.None, out outDateTime))
                    // Modified to assume that the datetime in the filename format (from iPhone iOS) is a UTC datetime - this will make sure the datetime gets
                    // converted to local datetime for an accurate datetime of when the photo was taken
                    if (DateTime.TryParseExact(dateStr, dateFormat, null, System.Globalization.DateTimeStyles.None, out outDateTime))
                    //if (DateTime.TryParseExact(dateStr, dateFormat, null, System.Globalization.DateTimeStyles.AssumeUniversal, out outDateTime))
                    {
                        //log($"{fileName}, date: {dateStr}, format: {dateFormat}, DateTime: {outDateTime}");
                    }
                    else
                    {
                        // >>>>> figure out how to bubble this error up so an error message can be shown in web UI
                        Console.WriteLine($"{fileName}, date: {dateStr}, format: {dateFormat}, *** PARSE FAILED ***");
                    }
                }

                index++;
            }

            return outDateTime;
        }

        private static string SerializeToCamelCaseJson<T>(T value)
        {
            return JsonConvert.SerializeObject(value, new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            });
        }

        private static async Task<HttpResponseData> CreateJsonResponse(HttpRequestData req, HttpStatusCode statusCode, object body)
        {
            var response = req.CreateResponse(statusCode);
            response.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await response.WriteStringAsync(SerializeToCamelCaseJson(body));
            return response;
        }

        private static Task<HttpResponseData> CreateErrorResponse(HttpRequestData req, HttpStatusCode statusCode, string message)
        {
            return CreateJsonResponse(req, statusCode, new { error = message });
        }

        [Function("GetSolarMetrics")]
        public async Task<HttpResponseData> GetSolarMetrics(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
        {
            try
            {
                // Read raw body
                string body = await new StreamReader(req.Body).ReadToEndAsync();

                // Deserialize JSON
                var paramData = Newtonsoft.Json.JsonConvert
                    .DeserializeObject<Dictionary<string, object>>(body);

                // Call your DB logic
                var solarMetrics = await dbCommon.GetSolarMetricsDB(paramData);

                // Create success response
                var response = req.CreateResponse(HttpStatusCode.OK);
                await response.WriteStringAsync(SerializeToCamelCaseJson(solarMetrics));
                return response;
            }
            catch (Exception ex)
            {
                log.LogError($"Exception in GetSolarMetrics: {ex.Message} {ex.StackTrace}");

                var errorResponse = req.CreateResponse(HttpStatusCode.BadRequest);
                await errorResponse.WriteStringAsync($"Exception, message = {ex.Message}");
                return errorResponse;
            }
        }


        // Public API for media info queries
        [Function("GetMediaInfo")]
        public async Task<HttpResponseData> GetMediaInfo(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequestData req)
        {
            try
            {
                string body = await new StreamReader(req.Body).ReadToEndAsync();
                // paramData is a JSON object with filter params
                var paramData = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
                var mediaInfoList = await dbCommon.GetMediaInfoDB(paramData);
                MediaInfoColl mediaInfoColl = new MediaInfoColl
                {
                    MediaInfoList = mediaInfoList,
                    isAdmin = authCheck.UserAuthorizedForRole(req, userAdminRole, out string userName)
                };
                return await CreateJsonResponse(req, HttpStatusCode.OK, mediaInfoColl);
            }
            catch (Exception ex)
            {
                log.LogError($"Exception in GetMediaInfo: {ex.Message} {ex.StackTrace}");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, $"Exception, message = {ex.Message}");
            }
        }

        // Public API for media info queries
        [Function("GetMediaAlbum")]
        public async Task<HttpResponseData> GetMediaAlbum(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequestData req)
        {
            try
            {
                string body = await new StreamReader(req.Body).ReadToEndAsync();
                // paramData is a JSON object with filter params
                var paramData = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, object>>(body);
                var mediaAlbumList = await dbCommon.GetMediaAlbumDB(paramData);
                return await CreateJsonResponse(req, HttpStatusCode.OK, mediaAlbumList);
            }
            catch (Exception ex)
            {
                log.LogError($"Exception in GetMediaAlbum: {ex.Message} {ex.StackTrace}");
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, $"Exception, message = {ex.Message}");
            }
        }

        [Function("UpdateMediaInfo")]
        public async Task<HttpResponseData> UpdateMediaInfo(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequestData req)
        {
            string userName = "";
            if (!authCheck.UserAuthorizedForRole(req, userAdminRole, out userName))
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "Unauthorized call - User does not have the correct Admin role");
            }

            //log.LogInformation($">>> User is authorized - userName: {userName}");

            //------------------------------------------------------------------------------------------------------------------
            // Parse the JSON payload content from the Request BODY into a C# object, and process the MediaInfo array to
            // find records to update
            //------------------------------------------------------------------------------------------------------------------
            string responseMessage = "";
            string databaseId = "jjkdb1";
            string containerId = "MediaInfo";

            try
            {
                var content = await new StreamReader(req.Body).ReadToEndAsync();
                var updParamData = JsonConvert.DeserializeObject<UpdateParamData>(content);
                if (updParamData == null)
                {
                    return await CreateJsonResponse(req, HttpStatusCode.OK, "Parameter content was NULL");
                }

                CosmosClient cosmosClient = new CosmosClient(apiCosmosDbConnStr);
                Database db = cosmosClient.GetDatabase(databaseId);
                Container container = db.GetContainer(containerId);

                var sql = "SELECT * FROM c WHERE c.id = @id ";
                int updCnt = 0;
                int tempIndex = -1;
                foreach (Item item in updParamData.MediaInfoFileList)
                {
                    tempIndex++;
                    if (updParamData.FileListIndex >= 0)
                    {
                        // Check for update of a particular specified file
                        if (tempIndex != updParamData.FileListIndex)
                        {
                            continue;
                        }
                    }
                    else
                    {
                        // If not a particular file, check for "selected" files to update
                        if (!item.selected)
                        {
                            continue;
                        }
                    }

                    // If FileListIndex is -999, then delete the record
                    if (updParamData.FileListIndex == -999)
                    {
                        await container.DeleteItemAsync<MediaInfo>(
                            id: item.id,
                            partitionKey: new PartitionKey(updParamData.MediaFilterMediaType)
                        );
                        updCnt++;
                    } 
                    else
                    {
                        // Build SQL query - Get the existing document from Cosmos DB (by the main unique "id")
                        var queryDef = new QueryDefinition(sql)
                            .WithParameter("@id", item.id);
                        var feed = container.GetItemQueryIterator<MediaInfo>(queryDef);
                        while (feed.HasMoreResults)
                        {
                            var response = await feed.ReadNextAsync();
                            foreach (var mediaInfo in response)
                            {
                                if (item.takenDateTime.Equals("USE_FILENAME", StringComparison.OrdinalIgnoreCase))
                                {
                                    loadDatePatterns();
                                    mediaInfo.TakenDateTime = getDateFromFilename(mediaInfo.Name);
                                    mediaInfo.TakenFileTime = int.Parse(mediaInfo.TakenDateTime.ToString("yyyyMMddHH"));
                                }
                                else
                                {
                                    mediaInfo.TakenDateTime = DateTime.Parse(item.takenDateTime);
                                    mediaInfo.TakenFileTime = int.Parse(mediaInfo.TakenDateTime.ToString("yyyyMMddHH"));
                                }

                                mediaInfo.CategoryTags = item.categoryTags;
                                mediaInfo.MenuTags = item.menuTags;
                                mediaInfo.AlbumTags = item.albumTags;
                                mediaInfo.Title = item.title;
                                mediaInfo.Description = item.description;
                                mediaInfo.People = item.people;
                                mediaInfo.SearchStr = mediaInfo.CategoryTags.ToLower() + " " +
                                        mediaInfo.MenuTags.ToLower() + " " +
                                        mediaInfo.Title.ToLower() + " " +
                                        mediaInfo.Description.ToLower() + " " +
                                        mediaInfo.People.ToLower();
                                await container.UpsertItemAsync(mediaInfo, new PartitionKey(mediaInfo.MediaTypeId));
                                updCnt++;
                            }
                        }
                    }
                }

                responseMessage = $"Number of recs updated = {updCnt}";
            }
            catch (Exception ex)
            {
                responseMessage = $"Exception in update, message = {ex.Message}";
            }

            return await CreateJsonResponse(req, HttpStatusCode.OK, responseMessage);
        }


        [Function("GetPeopleList")]
        public async Task<HttpResponseData> GetPeopleList(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequestData req)
        {
            string userName = "";
            if (!authCheck.UserAuthorizedForRole(req, userAdminRole, out userName))
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "Unauthorized call - User does not have the correct Admin role");
            }

            //log.LogInformation($">>> User is authorized - userName: {userName}");

            //------------------------------------------------------------------------------------------------------------------
            // Query the NoSQL container to get values
            //------------------------------------------------------------------------------------------------------------------
            string databaseId = "jjkdb1";
            string containerId = "MediaPeople";
            List<string> peopleList = new List<string>();

            try
            {
                CosmosClient cosmosClient = new CosmosClient(apiCosmosDbConnStr);
                Database db = cosmosClient.GetDatabase(databaseId);
                Container container = db.GetContainer(containerId);

                // Get the existing document from Cosmos DB
                var queryText = $"SELECT * FROM c ";
                var feed = container.GetItemQueryIterator<MediaPeople>(queryText);
                int cnt = 0;
                while (feed.HasMoreResults)
                {
                    var response = await feed.ReadNextAsync();
                    foreach (var mediaPeople in response)
                    {
                        cnt++;
                        //log.LogInformation($"{cnt}  Name: {mediaPeople.PeopleName}");
                        peopleList.Add(mediaPeople.PeopleName);
                    }
                }
            }
            catch (Exception ex)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, $"Exception in DB query to {containerId}, message = {ex.Message}");
            }

            return await CreateJsonResponse(req, HttpStatusCode.OK, peopleList);
        }


        [Function("GetGenvConfig")]
        public async Task<HttpResponseData> GetGenvConfig(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequestData req)
        {
            string userName = string.Empty;
            if (!authCheck.UserAuthorizedForRole(req, userAdminRole, out userName))
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, $"Unauthorized call - User does not have the correct Admin role, userName = {userName}");
            }
            //log.LogInformation($">>> User is authorized - userName: {userName}");

            //------------------------------------------------------------------------------------------------------------------
            // Query the NoSQL container to get values
            //------------------------------------------------------------------------------------------------------------------
            string databaseId = "jjkdb1";
            string containerId = "GenvConfig";
            var genvConfigList = new List<GenvConfig>();

            try
            {
                CosmosClient cosmosClient = new CosmosClient(apiCosmosDbConnStr);
                Database db = cosmosClient.GetDatabase(databaseId);
                Container container = db.GetContainer(containerId);

                // Get the content string from the HTTP request body
                string genvConfigId = await new StreamReader(req.Body).ReadToEndAsync();

                if (string.IsNullOrWhiteSpace(genvConfigId))
                {
                    // If no id is specified, get the last record only
                    var queryDefinition = new QueryDefinition("SELECT * FROM c ORDER BY c.ConfigId DESC OFFSET 0 LIMIT 1 ");
                    var feed = container.GetItemQueryIterator<GenvConfig>(queryDefinition);
                    while (feed.HasMoreResults)
                    {
                        var response = await feed.ReadNextAsync();
                        foreach (var item in response)
                        {
                            genvConfigList.Add(item);
                        }
                    }
                }
                else if (genvConfigId.Trim().Equals("History", StringComparison.OrdinalIgnoreCase))
                {
                    // If 'History' is specified, return all records ordered by ConfigId descending
                    var queryDefinition = new QueryDefinition("SELECT * FROM c ORDER BY c.ConfigId DESC");
                    var feed = container.GetItemQueryIterator<GenvConfig>(queryDefinition);
                    while (feed.HasMoreResults)
                    {
                        var response = await feed.ReadNextAsync();
                        foreach (var item in response)
                        {
                            genvConfigList.Add(item);
                        }
                    }
                }
                else
                {
                    // Get a single record by id
                    string id = genvConfigId.Trim();
                    int partitionKey = int.Parse(id);

                    ItemResponse<GenvConfig> response = await container.ReadItemAsync<GenvConfig>(
                        id: id,
                        partitionKey: new PartitionKey(partitionKey)
                    );

                    if (response.Resource != null)
                    {
                        genvConfigList.Add(response.Resource);
                    }
                }
            }
            catch (Exception ex)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, $"Exception in DB query to {containerId}, message = {ex.Message}");
            }

            return await CreateJsonResponse(req, HttpStatusCode.OK, genvConfigList);
        }

        [Function("GetGenvMetricPoint")]
        public async Task<HttpResponseData> GetGenvMetricPoint(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequestData req)
        {
            string userName = "";
            if (!authCheck.UserAuthorizedForRole(req, userAdminRole, out userName))
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "Unauthorized call - User does not have the correct Admin role");
            }

            //log.LogInformation($">>> User is authorized - userName: {userName}");

            //------------------------------------------------------------------------------------------------------------------
            // Query the NoSQL container to get values
            //------------------------------------------------------------------------------------------------------------------
            string databaseId = "jjkdb1";
            string containerId = "GenvMetricPoint";
            var genvMetricPoint = new GenvMetricPoint();

            try
            {
                CosmosClient cosmosClient = new CosmosClient(apiCosmosDbConnStr);
                Database db = cosmosClient.GetDatabase(databaseId);
                Container container = db.GetContainer(containerId);

                var queryDefinition = new QueryDefinition(
                    "SELECT * FROM c ORDER BY c._ts DESC OFFSET 0 LIMIT 1 ");

                // Get the existing document from Cosmos DB
                var feed = container.GetItemQueryIterator<GenvMetricPoint>(queryDefinition);
                int cnt = 0;
                while (feed.HasMoreResults)
                {
                    var response = await feed.ReadNextAsync();
                    foreach (var item in response)
                    {
                        cnt++;
                        genvMetricPoint = item;
                    }
                }
            }
            catch (Exception ex)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, $"Exception in DB query to {containerId}, message = {ex.Message}");
            }

            return await CreateJsonResponse(req, HttpStatusCode.OK, genvMetricPoint);
        }

        [Function("GetGenvMetrics")]
        public async Task<HttpResponseData> GetGenvMetrics(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequestData req)
        {
            string userName = "";
            if (!authCheck.UserAuthorizedForRole(req, userAdminRole, out userName))
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "Unauthorized call - User does not have the correct Admin role");
            }

            string databaseId = "jjkdb1";
            string containerId = "GenvMetricPoint";
            var results = new List<object>();

            try
            {
                // Read parameters from request body
                string body = await new StreamReader(req.Body).ReadToEndAsync();
                var paramData = JsonConvert.DeserializeObject<Dictionary<string, object>>(body ?? "{}") ?? new Dictionary<string, object>();

                int pointDateStartBucket = 0;
                int startDayTime = 0;
                int endDayTime = 0;
                int pointMaxRows = 5000;

                if (paramData.ContainsKey("pointDateStartBucket")) pointDateStartBucket = Convert.ToInt32(paramData["pointDateStartBucket"]);
                if (paramData.ContainsKey("startDayTime")) startDayTime = Convert.ToInt32(paramData["startDayTime"]);
                if (paramData.ContainsKey("endDayTime")) endDayTime = Convert.ToInt32(paramData["endDayTime"]);
                if (paramData.ContainsKey("pointMaxRows")) pointMaxRows = Convert.ToInt32(paramData["pointMaxRows"]);

                CosmosClient cosmosClient = new CosmosClient(apiCosmosDbConnStr);
                Database db = cosmosClient.GetDatabase(databaseId);
                Container container = db.GetContainer(containerId);

                // Build SQL query with parameters
                var sql = "SELECT c.PointDateTime, c.currTemperature, c.PointDayTime FROM c WHERE c.PointDay = @PointDay";
                if (startDayTime > 0)
                {
                    sql += " AND c.PointDayTime >= @StartDayTime";
                }
                if (endDayTime > 0)
                {
                    sql += " AND c.PointDayTime < @EndDayTime";
                }
                sql += " ORDER BY c.PointDateTime ASC OFFSET 0 LIMIT @MaxRows";

                var qd = new QueryDefinition(sql)
                    .WithParameter("@PointDay", pointDateStartBucket)
                    .WithParameter("@MaxRows", pointMaxRows);

                if (startDayTime > 0) qd = qd.WithParameter("@StartDayTime", startDayTime);
                if (endDayTime > 0) qd = qd.WithParameter("@EndDayTime", endDayTime);

                var feed = container.GetItemQueryIterator<JObject>(qd);
                while (feed.HasMoreResults)
                {
                    var response = await feed.ReadNextAsync();
                    foreach (var item in response)
                    {
                        // Convert JObject to plain object with the fields we need
                        var obj = new {
                            PointDateTime = item["PointDateTime"]?.ToString(),
                            currTemperature = item["currTemperature"]?.ToObject<double?>()
                        };
                        results.Add(obj);
                    }
                }
            }
            catch (Exception ex)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, $"Exception in DB query to {containerId}, message = {ex.Message}");
            }

            return await CreateJsonResponse(req, HttpStatusCode.OK, results);
        }

        [Function("GetGenvSelfie")]
        public async Task<HttpResponseData> GetGenvSelfie(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = null)] HttpRequestData req)
        {
            string userName = "";
            if (!authCheck.UserAuthorizedForRole(req, userAdminRole, out userName))
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "Unauthorized call - User does not have the correct Admin role");
            }

            //log.LogInformation($">>> User is authorized - userName: {userName}");

            //------------------------------------------------------------------------------------------------------------------
            // Query the NoSQL container to get values
            //------------------------------------------------------------------------------------------------------------------
            string databaseId = "jjkdb1";
            string containerId = "GenvImage";
            string base64ImgData = "";

            try
            {
                CosmosClient cosmosClient = new CosmosClient(apiCosmosDbConnStr);
                Database db = cosmosClient.GetDatabase(databaseId);
                Container container = db.GetContainer(containerId);

                var currDateTime = DateTime.Now;
                //    "SELECT * FROM c WHERE c.PointDay = @PointDay ORDER BY c.PointDayTime DESC")
                var queryDefinition = new QueryDefinition(
                    "SELECT * FROM c WHERE c.PointDay = @PointDay ORDER BY c._ts DESC OFFSET 0 LIMIT 1 ")
                    .WithParameter("@PointDay", int.Parse(currDateTime.ToString("yyyyMMdd")));
                // Get the existing document from Cosmos DB
                //var queryText = $"SELECT * FROM c ";
                var feed = container.GetItemQueryIterator<GenvImage>(queryDefinition);
                int cnt = 0;
                bool done = false;
                while (feed.HasMoreResults && !done)
                {
                    var response = await feed.ReadNextAsync();
                    foreach (var item in response)
                    {
                        cnt++;
                        //log.LogInformation($"{cnt}  id: {genvConfig.id}");
                        /*
                            "id": "c55162e0-0717-485a-8598-cb69605000ea",
                            "PointDay": 20250517,
                            "PointDateTime": "2025-05-17 00:08:43",
                            "PointDayTime": 25000843,
                            "ImgData": 
                        */
                        // Get the string value of base64 image data from the most recent photo
                        base64ImgData = item.ImgData;
                        done = true;
                    }
                }
            }
            catch (Exception ex)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, $"Exception in DB query to {containerId}, message = {ex.Message}");
            }

            return await CreateJsonResponse(req, HttpStatusCode.OK, base64ImgData);
        }

        private class RequestCommandParamData
        {
            public int ConfigId { get; set; } // Partition key (1)
            public string RequestCommand { get; set; }
            public string RequestValue { get; set; }
            public override string ToString()
            {
                return JsonConvert.SerializeObject(this);
            }
        }

        [Function("GenvRequestCommand")]
        public async Task<HttpResponseData> GenvRequestCommand(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequestData req)
        {
            string userName = "";
            if (!authCheck.UserAuthorizedForRole(req, userAdminRole, out userName))
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "Unauthorized call - User does not have the correct Admin role");
            }

            //log.LogInformation($">>> User is authorized - userName: {userName}");

            //------------------------------------------------------------------------------------------------------------------
            // Parse the JSON payload content from the Request BODY into a string
            //------------------------------------------------------------------------------------------------------------------
            string responseMessage = "";
            string databaseId = "jjkdb1";
            string containerId = "GenvCommandRequest";
            var genvCommandRequest = new GenvCommandRequest();

            try
            {
                var content = await new StreamReader(req.Body).ReadToEndAsync();
                var paramData = JsonConvert.DeserializeObject<RequestCommandParamData>(content);
                if (paramData == null)
                {
                    return await CreateJsonResponse(req, HttpStatusCode.OK, "Parameter content was NULL");
                }

                CosmosClient cosmosClient = new CosmosClient(apiCosmosDbConnStr);
                Database db = cosmosClient.GetDatabase(databaseId);
                Container container = db.GetContainer(containerId);

                genvCommandRequest.id = Guid.NewGuid().ToString(); // Generate a new unique id
                genvCommandRequest.ConfigId = paramData.ConfigId; // Partition key (1)
                genvCommandRequest.processed = false; // Initially not processed
                genvCommandRequest.requestCommand = paramData.RequestCommand;
                genvCommandRequest.requestValue = paramData.RequestValue;
                genvCommandRequest.requestResult = "Pending"; // Initial status

                genvCommandRequest.requestTime = DateTime.UtcNow; // Set the request time

                await container.CreateItemAsync(
                    genvCommandRequest,
                    new PartitionKey(genvCommandRequest.ConfigId)
                );

                responseMessage = $"Command requested = {paramData.RequestCommand}, Val = {paramData.RequestValue}";
            }
            catch (Exception ex)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, $"Exception in DB to {containerId}, message = {ex.Message}");
            }

            return await CreateJsonResponse(req, HttpStatusCode.OK, responseMessage);
        } // GenvRequestCommand


        public void AddPatchField(List<PatchOperation> patchOperations, Dictionary<string, string> formFields, string fieldName, string fieldType = "Text", string operationType = "Replace")
        {
            if (patchOperations == null || formFields == null || string.IsNullOrWhiteSpace(fieldName))
                return; // Prevent potential null reference errors

            if (operationType.Equals("Replace", StringComparison.OrdinalIgnoreCase))
            {
                if (fieldType.Equals("Text"))
                {
                    if (formFields.ContainsKey(fieldName))
                    {
                        string value = formFields[fieldName]?.Trim() ?? string.Empty;
                        patchOperations.Add(PatchOperation.Replace("/" + fieldName, value));
                    }
                }
                else if (fieldType.Equals("Int"))
                {
                    if (formFields.ContainsKey(fieldName))
                    {
                        string value = formFields[fieldName]?.Trim() ?? string.Empty;
                        patchOperations.Add(PatchOperation.Replace("/" + fieldName, int.Parse(value)));
                    }
                }
                else if (fieldType.Equals("Money"))
                {
                    string value = formFields[fieldName]?.Trim() ?? string.Empty;
                    //string input = "$1,234.56";
                    if (decimal.TryParse(value, NumberStyles.Currency, CultureInfo.GetCultureInfo("en-US"), out decimal moneyVal))
                    {
                        Console.WriteLine($"Parsed currency: {moneyVal}");
                        patchOperations.Add(PatchOperation.Replace("/" + fieldName, moneyVal));
                    }
                }
                else if (fieldType.Equals("Bool"))
                {
                    int value = 0;
                    if (formFields.ContainsKey(fieldName))
                    {
                        string checkedValue = formFields[fieldName]?.Trim() ?? string.Empty;
                        if (checkedValue.Equals("on"))
                        {
                            value = 1;
                        }
                    }
                    patchOperations.Add(PatchOperation.Replace("/" + fieldName, value));
                }
            }
            else if (operationType.Equals("Add", StringComparison.OrdinalIgnoreCase))
            {
                //string value = formFields[fieldName]?.Trim() ?? string.Empty;
                //patchOperations.Add(PatchOperation.Add("/" + fieldName, value));

                if (fieldType.Equals("Text"))
                {
                    if (formFields.ContainsKey(fieldName))
                    {
                        string value = formFields[fieldName]?.Trim() ?? string.Empty;
                        patchOperations.Add(PatchOperation.Add("/" + fieldName, value));
                    }
                }
                else if (fieldType.Equals("Int"))
                {
                    if (formFields.ContainsKey(fieldName))
                    {
                        string value = formFields[fieldName]?.Trim() ?? string.Empty;
                        patchOperations.Add(PatchOperation.Add("/" + fieldName, int.Parse(value)));
                    }
                }
                else if (fieldType.Equals("Bool"))
                {
                    int value = 0;
                    if (formFields.ContainsKey(fieldName))
                    {
                        string checkedValue = formFields[fieldName]?.Trim() ?? string.Empty;
                        if (checkedValue.Equals("on"))
                        {
                            value = 1;
                        }
                    }
                    patchOperations.Add(PatchOperation.Add("/" + fieldName, value));
                }
            }
            else if (operationType.Equals("Remove", StringComparison.OrdinalIgnoreCase))
            {
                patchOperations.Add(PatchOperation.Remove("/" + fieldName));
            }
        }


        public T GetFieldValue<T>(Dictionary<string, string> formFields, string fieldName, T defaultValue = default)
        {
            if (formFields == null || string.IsNullOrWhiteSpace(fieldName))
                return defaultValue;

            if (formFields.TryGetValue(fieldName, out string rawValue))
            {
                try
                {
                    if (typeof(T) == typeof(bool))
                    {
                        object boolValue = rawValue.Trim().Equals("on", StringComparison.OrdinalIgnoreCase);
                        return (T)boolValue;
                    }
                    else
                    {
                        return (T)Convert.ChangeType(rawValue.Trim(), typeof(T));
                    }
                }
                catch
                {
                    // Optionally log the error here
                    return defaultValue;
                }
            }

            return defaultValue;
        }

        public bool GetFieldValueBool(Dictionary<string, string> formFields, string fieldName)
        {
            bool value = false;
            if (formFields == null || string.IsNullOrWhiteSpace(fieldName))
                return value; // Prevent potential null reference errors

            if (formFields.ContainsKey(fieldName))
            {
                string checkedValue = formFields[fieldName]?.Trim() ?? string.Empty;
                if (checkedValue.Equals("on"))
                {
                    value = true;
                }
            }
            return value;
        }
        public decimal GetFieldValueMoney(Dictionary<string, string> formFields, string fieldName)
        {
            decimal value = 0.00m;
            if (formFields == null || string.IsNullOrWhiteSpace(fieldName))
                return value; // Prevent potential null reference errors

            if (formFields.ContainsKey(fieldName))
            {
                string rawValue = formFields[fieldName]?.Trim() ?? string.Empty;
                //string input = "$1,234.56";
                if (decimal.TryParse(rawValue, NumberStyles.Currency, CultureInfo.GetCultureInfo("en-US"), out decimal moneyVal))
                {
                    //Console.WriteLine($"Parsed currency: {moneyVal}");
                }
                value = moneyVal;
            }
            return value;
        }


        [Function("UpdateGenvConfig")]
        public async Task<HttpResponseData> UpdateGenvConfig(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = null)] HttpRequestData req)
        {
            string userName = "";
            if (!authCheck.UserAuthorizedForRole(req, userAdminRole, out userName))
            {
                return await CreateErrorResponse(req, HttpStatusCode.Unauthorized, "Unauthorized call - User does not have the correct Admin role");
            }

            //log.LogInformation($">>> User is authorized - userName: {userName}");

            //------------------------------------------------------------------------------------------------------------------
            // Parse the JSON payload content from the Request BODY into a string
            //------------------------------------------------------------------------------------------------------------------
            string databaseId = "jjkdb1";
            string containerId = "GenvConfig";
            GenvConfig genvConfig = new GenvConfig();

            try
            {
                // Get content from the Request BODY
                var boundary = HeaderUtilities.RemoveQuotes(MediaTypeHeaderValue.Parse(req.Headers.GetValues("Content-Type").FirstOrDefault()).Boundary).Value;
                var reader = new MultipartReader(boundary, req.Body);
                var section = await reader.ReadNextSectionAsync();

                var formFields = new Dictionary<string, string>();
                var files = new List<(string fieldName, string fileName, byte[] content)>();

                while (section != null)
                {
                    var contentDisposition = section.GetContentDispositionHeader();
                    if (contentDisposition != null)
                    {
                        if (contentDisposition.IsFileDisposition())
                        {
                            using var memoryStream = new MemoryStream();
                            await section.Body.CopyToAsync(memoryStream);
                            files.Add((contentDisposition.Name.Value, contentDisposition.FileName.Value, memoryStream.ToArray()));
                        }
                        else if (contentDisposition.IsFormDisposition())
                        {
                            using var streamReader = new StreamReader(section.Body);
                            formFields[contentDisposition.Name.Value] = await streamReader.ReadToEndAsync();
                        }
                    }

                    section = await reader.ReadNextSectionAsync();
                }

                CosmosClient cosmosClient = new CosmosClient(apiCosmosDbConnStr);
                Database db = cosmosClient.GetDatabase(databaseId);
                Container container = db.GetContainer(containerId);

                DateTime currDateTime = DateTime.Now;
                string LastChangedTs = currDateTime.ToString("o");

                //------------------------------------------------------------------------------------------------------------------
                // Query the NoSQL container to get current values
                //------------------------------------------------------------------------------------------------------------------
                string id = formFields["updId"].Trim();
                int configId = int.Parse(formFields["configId"].Trim());

                genvConfig = await container.ReadItemAsync<GenvConfig>(id, new PartitionKey(configId));

                // Overwrite values from formFields
                genvConfig.configDesc = GetFieldValue<string>(formFields, "configDesc");
                genvConfig.loggingOn = GetFieldValueBool(formFields, "loggingSwitch");
                genvConfig.selfieOn = GetFieldValueBool(formFields, "imagesSwitch");
                genvConfig.commandRequestOn = GetFieldValueBool(formFields, "commandRequestSwitch");
                genvConfig.daysToBloom = GetFieldValue<int>(formFields, "daysToBloom");
                // 2025-07-06 - this was done with Co-Pilot AI agent in VS Code - pretty cool!
                genvConfig.daysToGerm = GetFieldValue<string>(formFields, "daysToGerm");
                genvConfig.daysToBloom = GetFieldValue<int>(formFields, "daysToBloom");
                genvConfig.germinationStart = GetFieldValue<string>(formFields, "germinationStart");
                genvConfig.plantingDate = GetFieldValue<string>(formFields, "plantingDate");

                // Recalculate harvest, cure, and production dates (based on planting date and days to bloom)
                DateTime plantingDate = DateTime.Parse(GetFieldValue<string>(formFields, "plantingDate"));
                int daysToBloom = GetFieldValue<int>(formFields, "daysToBloom");
                genvConfig.harvestDate = plantingDate.AddDays(daysToBloom).ToString("yyyy-MM-dd");
                genvConfig.cureDate = plantingDate.AddDays(daysToBloom + 14).ToString("yyyy-MM-dd");
                genvConfig.productionDate = plantingDate.AddDays(daysToBloom + 21).ToString("yyyy-MM-dd");

                genvConfig.logMetricInterval = GetFieldValue<int>(formFields, "logMetricInterval");
                genvConfig.targetTemperature = GetFieldValue<float>(formFields, "targetTemperature");
                genvConfig.currTemperature = GetFieldValue<float>(formFields, "currTemperature");
                genvConfig.airInterval = GetFieldValue<float>(formFields, "airInterval");
                genvConfig.airDuration = GetFieldValue<float>(formFields, "airDuration");
                genvConfig.heatInterval = GetFieldValue<float>(formFields, "heatInterval");
                genvConfig.heatDuration = GetFieldValue<float>(formFields, "heatDuration");
                genvConfig.waterInterval = GetFieldValue<float>(formFields, "waterInterval");
                genvConfig.waterDuration = GetFieldValue<float>(formFields, "waterDuration");
                genvConfig.lightDuration = GetFieldValue<float>(formFields, "lightDuration");
                genvConfig.notes = GetFieldValue<string>(formFields, "notes");
                genvConfig.s0day = GetFieldValue<int>(formFields, "s0day");
                genvConfig.s0waterDuration = GetFieldValue<int>(formFields, "s0waterDuration");
                genvConfig.s0waterInterval = GetFieldValue<int>(formFields, "s0waterInterval");
                genvConfig.s1day = GetFieldValue<int>(formFields, "s1day");
                genvConfig.s1waterDuration = GetFieldValue<int>(formFields, "s1waterDuration");
                genvConfig.s1waterInterval = GetFieldValue<int>(formFields, "s1waterInterval");
                genvConfig.s2day = GetFieldValue<int>(formFields, "s2day");
                genvConfig.s2waterDuration = GetFieldValue<int>(formFields, "s2waterDuration");
                genvConfig.s2waterInterval = GetFieldValue<int>(formFields, "s2waterInterval");
                genvConfig.s3day = GetFieldValue<int>(formFields, "s3day");
                genvConfig.s3waterDuration = GetFieldValue<int>(formFields, "s3waterDuration");
                genvConfig.s3waterInterval = GetFieldValue<int>(formFields, "s3waterInterval");
                genvConfig.s4day = GetFieldValue<int>(formFields, "s4day");
                genvConfig.s4waterDuration = GetFieldValue<int>(formFields, "s4waterDuration");
                genvConfig.s4waterInterval = GetFieldValue<int>(formFields, "s4waterInterval");
                genvConfig.s5day = GetFieldValue<int>(formFields, "s5day");
                genvConfig.s5waterDuration = GetFieldValue<int>(formFields, "s5waterDuration");
                genvConfig.s5waterInterval = GetFieldValue<int>(formFields, "s5waterInterval");
                genvConfig.s6day = GetFieldValue<int>(formFields, "s6day");
                genvConfig.s6waterDuration = GetFieldValue<int>(formFields, "s6waterDuration");
                genvConfig.s6waterInterval = GetFieldValue<int>(formFields, "s6waterInterval");

                await container.ReplaceItemAsync(genvConfig, genvConfig.id, new PartitionKey(genvConfig.ConfigId));

            }
            catch (Exception ex)
            {
                return await CreateErrorResponse(req, HttpStatusCode.BadRequest, $"Exception in DB to {containerId}, message = {ex.Message}");
            }

            return await CreateJsonResponse(req, HttpStatusCode.OK, genvConfig);
        } // UpdateGenvConfig



    } // public class WebApi

} // namespace namespace jjkWebFunctions2

