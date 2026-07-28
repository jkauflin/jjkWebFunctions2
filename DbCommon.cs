/*==============================================================================
(C) Copyright 2025 John J Kauflin, All rights reserved.
--------------------------------------------------------------------------------
DESCRIPTION:  Common functions to handle interaction with the data source, and
              Blob storage. Centralize all data source libraries and 
              configuration to this utility class.
--------------------------------------------------------------------------------
Modification History
2025-10-24 JJK  Initial versions
                *** NEW philosophy - put the error handling (try/catch) in the
                main/calling function, and leave it out of the DB Common - DB
                Common will throw any error, and the caller can log and handle
================================================================================*/
using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Cosmos;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using jjkWebFunctions2.Model;

namespace jjkWebFunctions2
{
    public class DbCommon
    {
        private readonly ILogger log;
        private readonly IConfiguration config;
        private readonly string? apiCosmosDbConnStr;
        private readonly string? apiStorageConnStr;
        private readonly string databaseId;

        public DbCommon(ILogger logger, IConfiguration configuration)
        {
            log = logger;
            config = configuration;
            apiCosmosDbConnStr = config["API_COSMOS_DB_CONN_STR"];
            apiStorageConnStr = config["AzureWebJobsStorage"];
            databaseId = "jjkdb1";
        }
        
        public async Task<SolarMetrics> GetSolarMetricsDB(Dictionary<string, object> paramData)
        {
            CosmosClient cosmosClient = new CosmosClient(apiCosmosDbConnStr);
            Database db = cosmosClient.GetDatabase(databaseId);
            Container container = db.GetContainer("MetricPoint");

            //log.LogWarning("-------------------------------------------------------------------------------------------------------------------------------------------");
            //log.LogWarning($">>> paramData: {Newtonsoft.Json.JsonConvert.SerializeObject(paramData)}");

            int pointDay = paramData.ContainsKey("pointDateStartBucket") ? Convert.ToInt32(paramData["pointDateStartBucket"]) : 1;
            int pointDayTime = paramData.ContainsKey("pointDayTime") ? Convert.ToInt32(paramData["pointDayTime"]) : 0;
            int pointMaxRows = paramData.ContainsKey("pointMaxRows") ? Convert.ToInt32(paramData["pointMaxRows"]) : 500;
            int dayTotalStartBucket = paramData.ContainsKey("dayTotalStartBucket") ? Convert.ToInt32(paramData["dayTotalStartBucket"]) : 1;
            int dayTotalMaxRows = paramData.ContainsKey("dayTotalMaxRows") ? Convert.ToInt32(paramData["dayTotalMaxRows"]) : 14;

            // Initialize return object and lists
            var solarMetrics = new SolarMetrics();
            solarMetrics.pointList = new List<MetricPoint>();
            solarMetrics.totalList = new List<MetricTotal>();
            solarMetrics.yearTotalList = new List<MetricYearTotal>();

            // Request options: MaxItemCount controls page size (not total rows)
            QueryRequestOptions queryRequestOptions = new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(pointDay),
                MaxItemCount = pointMaxRows // Page Count - each page will return up to MaxItemCount items
            };

            // Build SQL query
            string sql = "SELECT TOP @maxRows * FROM c WHERE c.PointDayTime >= @pointDayTime ORDER BY c.PointDayTime ASC";

            var queryDef = new QueryDefinition(sql)
                .WithParameter("@maxRows", pointMaxRows)
                .WithParameter("@pointDayTime", pointDayTime);

            var feed = container.GetItemQueryIterator<MetricPoint>(queryDef,requestOptions: queryRequestOptions);
            int pageCnt = 0;
            int rowCnt = 0;
            while (feed.HasMoreResults)
            {
                pageCnt++;
                //log.LogWarning($"------- Reading page {pageCnt} ...");
                var response = await feed.ReadNextAsync();
                foreach (var item in response)
                {
                    rowCnt++;
                    solarMetrics.pointList.Add(item);
                    //log.LogWarning($">>> {rowCnt} {item.PointDay}, PointDateTime: {item.PointDateTime}, pvWatts: {item.pvWatts}");
                }
            }

            //-------------------------------------------------------------------------------------------------------------------------------------
            // Get the Day Totals
            //-------------------------------------------------------------------------------------------------------------------------------------
            container = db.GetContainer("MetricTotal");

            queryRequestOptions = new QueryRequestOptions
            {
                //PartitionKey = new PartitionKey(pointDay),
                MaxItemCount = dayTotalMaxRows  // Page Count - each page will return up to MaxItemCount items
            };

            // Build SQL query
            sql = "SELECT TOP @maxRows * FROM c WHERE c.id = @totalsId AND c.TotalBucket >= @dayTotalStartBucket ORDER BY c.TotalBucket ASC";
            queryDef = new QueryDefinition(sql)
                .WithParameter("@maxRows", dayTotalMaxRows)
                .WithParameter("@totalsId", "DAY")
                .WithParameter("@dayTotalStartBucket", dayTotalStartBucket);

            //log.LogWarning($">>> queryDef.QueryText: {queryDef.QueryText}");
            //log.LogWarning($">>> dayTotalStartBucket: {dayTotalStartBucket}");

            var feed2 = container.GetItemQueryIterator<MetricTotal>(queryDef,requestOptions: queryRequestOptions);
            pageCnt = 0;
            rowCnt = 0;
            while (feed2.HasMoreResults)
            {
                pageCnt++;
                //log.LogWarning($"------- Reading page {pageCnt} ...");
                var response = await feed2.ReadNextAsync();
                foreach (var item in response)
                {
                    rowCnt++;
                    solarMetrics.totalList.Add(item);
                    //log.LogWarning($">>> {rowCnt} {item.TotalBucket}, TotalValue: {item.TotalValue}, AmpMaxValue: {item.AmpMaxValue}, WattMaxValue: {item.WattMaxValue}");
                }
            }

            //-------------------------------------------------------------------------------------------------------------------------------------
            // Get the Year Totals
            //-------------------------------------------------------------------------------------------------------------------------------------
            container = db.GetContainer("MetricYearTotal");

            queryRequestOptions = new QueryRequestOptions
            {
                //PartitionKey = new PartitionKey(pointDay),
                MaxItemCount = dayTotalMaxRows  // Page Count - each page will return up to MaxItemCount items
            };

            // Build SQL query
            sql = "SELECT * FROM c WHERE c.id = @totalsId ORDER BY c.TotalBucket ASC";
            queryDef = new QueryDefinition(sql)
                .WithParameter("@totalsId", "YEAR");

            var feed3 = container.GetItemQueryIterator<MetricYearTotal>(queryDef,requestOptions: queryRequestOptions);
            pageCnt = 0;
            rowCnt = 0;
            while (feed3.HasMoreResults)
            {
                pageCnt++;
                //log.LogWarning($"------- Reading page {pageCnt} ...");
                var response = await feed3.ReadNextAsync();
                foreach (var item in response)
                {
                    rowCnt++;
                    solarMetrics.yearTotalList.Add(item);
                    //log.LogWarning($">>> {rowCnt} {item.TotalBucket}, TotalValue: {item.TotalValue}");
                }
            }

            return solarMetrics;
        }


        /*
        // Common internal function to lookup configuration values
        private async Task<string> getConfigVal(Container container, string configName)
        {
            string configVal = "";
            string sql = $"SELECT * FROM c WHERE c.ConfigName = '{configName}' ";
            var feed = container.GetItemQueryIterator<hoa_config>(sql);
            while (feed.HasMoreResults)
            {
                var response = await feed.ReadNextAsync();
                foreach (var item in response)
                {
                    configVal = item.ConfigValue ?? "";
                }
            }
            return configVal;
        }

        public async Task<List<HoaProperty>> GetPropertyList(string searchStr)
        {
            // Construct the query from the parameters
            searchStr = searchStr.Trim().ToUpper();
            string sql = $"SELECT * FROM c WHERE "
                        + $"CONTAINS(UPPER(c.Parcel_ID),'{searchStr}') "
                        + $"OR CONTAINS(UPPER(c.LotNo),'{searchStr}') "
                        + $"OR CONTAINS(UPPER(c.Parcel_Location),'{searchStr}') "
                        + $"OR CONTAINS(UPPER(CONCAT(c.Owner_Name1,' ',c.Owner_Name2,' ',c.Mailing_Name)),'{searchStr}') "
                        + $"ORDER BY c.id";

            //------------------------------------------------------------------------------------------------------------------
            // Query the NoSQL container to get values
            //------------------------------------------------------------------------------------------------------------------
            string containerId = "hoa_properties";
            List<HoaProperty> hoaPropertyList = new List<HoaProperty>();
            HoaProperty hoaProperty = new HoaProperty();

            CosmosClient cosmosClient = new CosmosClient(apiCosmosDbConnStr);
            Database db = cosmosClient.GetDatabase(databaseId);
            Container container = db.GetContainer(containerId);

            var feed = container.GetItemQueryIterator<hoa_properties>(sql);
            int cnt = 0;
            while (feed.HasMoreResults)
            {
                var response = await feed.ReadNextAsync();
                foreach (var item in response)
                {
                    cnt++;
                    hoaProperty = new HoaProperty();
                    hoaProperty.parcelId = item.Parcel_ID;
                    hoaProperty.lotNo = item.LotNo;
                    hoaProperty.subDivParcel = item.SubDivParcel;
                    hoaProperty.parcelLocation = item.Parcel_Location;
                    hoaProperty.ownerName = item.Owner_Name1 + " " + item.Owner_Name2;
                    hoaProperty.ownerPhone = item.Owner_Phone;
                    hoaPropertyList.Add(hoaProperty);
                }
            }

            return hoaPropertyList;
        }
        */
        

        // Query Cosmos DB for MediaInfo records based on paramData
        public async Task<List<MediaInfo>> GetMediaInfoDB(Dictionary<string, object> paramData)
        {
            CosmosClient cosmosClient = new CosmosClient(apiCosmosDbConnStr);
            Database db = cosmosClient.GetDatabase(databaseId);
            Container container = db.GetContainer("MediaInfo");

            //log.LogWarning($">>> GetMediaInfoDB paramData: {Newtonsoft.Json.JsonConvert.SerializeObject(paramData)}");

            var mediaInfoList = new List<MediaInfo>();

            // figure out how to get the MediaTypeId and set the getMenu flag
            int mediaTypeId = paramData.ContainsKey("MediaFilterMediaType") ? Convert.ToInt32(paramData["MediaFilterMediaType"]) : 1;
            string category = paramData.ContainsKey("MediaFilterCategory") ? (paramData["MediaFilterCategory"]?.ToString() ?? "") : "";
            string startDate = paramData.ContainsKey("MediaFilterStartDate") ? (paramData["MediaFilterStartDate"]?.ToString() ?? "") : "";
            string menuItem = paramData.ContainsKey("MediaFilterMenuItem") ? (paramData["MediaFilterMenuItem"]?.ToString() ?? "") : "";
            string albumKey = paramData.ContainsKey("MediaFilterAlbumKey") ? (paramData["MediaFilterAlbumKey"]?.ToString() ?? "") : "";
            string searchStr = paramData.ContainsKey("MediaFilterSearchStr") ? (paramData["MediaFilterSearchStr"]?.ToString().ToLower() ?? "") : "";
            //bool getMenu = paramData.ContainsKey("getMenu") ? Convert.ToBoolean(paramData["getMenu"]) : false;

            if (albumKey != "")
            {
                category = "ALL";
                menuItem = "";
            }

            int maxRows = 150;
            if (paramData.ContainsKey("maxRows"))
            {
                maxRows = Convert.ToInt32(paramData["maxRows"]);
            }
            else
            {
                if (mediaTypeId == 2) {
                    //maxRows = 18;
                    maxRows = 12;
                }
            }

            if (!string.IsNullOrEmpty(category) && category != "ALL" && category != "0")
            {
                // Adjust category to remove " Family" or " family" suffix if present
                {
                    // Remove trailing " Family" (case-insensitive) if present, then trim
                    const string familySuffix = " Family";
                    if (!string.IsNullOrEmpty(category) && category.EndsWith(familySuffix, System.StringComparison.OrdinalIgnoreCase))
                    {
                        category = category.Substring(0, category.Length - familySuffix.Length).Trim();
                    }
                }
            }

            //log.LogWarning("-------------------------------------------------------------------------------------------------------------------------------------------");
            //log.LogWarning($">>> Filter params: MediaTypeId: {mediaTypeId}, Category: {category}, StartDate: {startDate}, maxRows: {maxRows}");
            //log.LogWarning($">>> Filter params: menuItem: {menuItem}, albumKey: {albumKey}, searchStr: {searchStr}");

            //-------------------------------------------------------------------------------------------------------------------------------------
            // Build query to get MediaInfo records based on filter parameters
            //-------------------------------------------------------------------------------------------------------------------------------------
            // Request options: MaxItemCount controls page size (not total rows)
            QueryRequestOptions queryRequestOptions = new QueryRequestOptions
            {
                PartitionKey = new PartitionKey(mediaTypeId),
                MaxItemCount = maxRows // Page Count - each page will return up to rowMax items
            };

            // Build SQL query
            string sql = "SELECT TOP @maxRows * FROM c WHERE c.MediaTypeId = @mediaTypeId";

            if (!string.IsNullOrEmpty(category) && category != "ALL" && category != "0")
            {
                sql += " AND CONTAINS(c.CategoryTags, @category)";
            }
            long startDateVal = 0;
            if (!string.IsNullOrEmpty(startDate) && DateTime.TryParse(startDate, out DateTime outDateTime))
            {
                startDateVal = long.Parse(outDateTime.ToString("yyyyMMddHH"));
                sql += " AND c.TakenFileTime >= @startDateVal";
            }
            if (!string.IsNullOrEmpty(menuItem))
            {
                sql += " AND CONTAINS(c.MenuTags, @menuItem)";
            }
            if (!string.IsNullOrEmpty(albumKey))
            {
                sql += " AND CONTAINS(c.AlbumTags, @albumKey)";
            }
            if (!string.IsNullOrEmpty(searchStr))
            {
                sql += " AND CONTAINS(c.SearchStr, @searchStr)";
            }

            //sql += " ORDER BY c.MediaDateTimeVal DESC ";
            if (mediaTypeId == 2)
            {
                sql += " ORDER BY c.Name ";
            }
            else
            {
                sql += " ORDER BY c.TakenDateTime ";
            }

            //log.LogWarning($"*** maxRows: {maxRows}, SQL: {sql}");

            var queryDef = new QueryDefinition(sql)
                .WithParameter("@maxRows", maxRows)
                .WithParameter("@mediaTypeId", mediaTypeId);

            if (!string.IsNullOrEmpty(category) && category != "ALL" && category != "0")
            {
                queryDef = queryDef.WithParameter("@category", category);
            }
            if (startDateVal > 0) {
                queryDef = queryDef.WithParameter("@startDateVal", startDateVal);
            }
            if (!string.IsNullOrEmpty(menuItem))
            {
                queryDef = queryDef.WithParameter("@menuItem", menuItem);
            }
            if (!string.IsNullOrEmpty(albumKey))
            {
                queryDef = queryDef.WithParameter("@albumKey", albumKey);
            }
            if (!string.IsNullOrEmpty(searchStr))
            {
                queryDef = queryDef.WithParameter("@searchStr", searchStr);
            }

            var feed = container.GetItemQueryIterator<MediaInfo>(queryDef, requestOptions: queryRequestOptions);
            int pageCnt = 0;
            int rowCnt = 0;
            while (feed.HasMoreResults)
            {
                pageCnt++;
                //log.LogWarning($"------- Reading page {pageCnt} ...");
                var response = await feed.ReadNextAsync();
                foreach (var item in response)
                {
                    rowCnt++;
                    mediaInfoList.Add(item);
                    //log.LogWarning($">>> {rowCnt} {item.Name}, MediaDateTime: {item.MediaDateTime}, CategoryTags: {item.CategoryTags}");    
                }
            }

            return mediaInfoList;
        }

        // Query Cosmos DB for MediaInfo records based on paramData
        public async Task<List<MediaAlbum>> GetMediaAlbumDB(Dictionary<string, object> paramData)
        {
            CosmosClient cosmosClient = new CosmosClient(apiCosmosDbConnStr);
            Database db = cosmosClient.GetDatabase(databaseId);
            Container container = db.GetContainer("MediaAlbum");

            //log.LogWarning($">>> GetMediaInfoDB paramData: {Newtonsoft.Json.JsonConvert.SerializeObject(paramData)}");

            var albumList = new List<MediaAlbum>();
            //var albumQuery = new QueryDefinition("SELECT * FROM c ORDER BY c.AlbumName");
            var albumQuery = new QueryDefinition("SELECT * FROM c ORDER BY c.AlbumKey");
            var albumFeed = container.GetItemQueryIterator<MediaAlbum>(albumQuery);
            while (albumFeed.HasMoreResults)
            {
                var response = await albumFeed.ReadNextAsync();
                foreach (var item in response)
                {
                    albumList.Add(item);
                }
            }

            return albumList;
        }

    } // public class DbCommon
} // namespace

