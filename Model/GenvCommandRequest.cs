
using System;
using Newtonsoft.Json;

namespace jjkWebFunctions2.Model
{
    public class GenvCommandRequest
    {
        public string id { get; set; }                  // Just "1"
        public int ConfigId { get; set; }               // Partition key (1)
        public bool processed { get; set; }
        public string requestCommand { get; set; }      // e.g., "WaterOn"
        public string requestValue { get; set; }       // e.g., "30" for 30 seconds
        public string requestResult { get; set; }      // e.g., "Success" or "Failed"
        public DateTime requestTime { get; set; }       // Time of the request
        public DateTime responseTime { get; set; }      // Time of the response
        
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

}
