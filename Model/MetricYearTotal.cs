
using System;
using Newtonsoft.Json;

namespace jjkWebFunctions2.Model
{
    public class MetricYearTotal
    {
        /*
    "id": "YEAR",
    "TotalBucket": 2024,
    "LastUpdateDateTime": "2024-12-31T20:59:51.8519188-05:00",
    "TotalValue": "1770.544",
        */
        public string id { get; set; }
        public int TotalBucket { get; set; }
        public string? LastUpdateDateTime { get; set; }
        public float TotalValue { get; set; }
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}
