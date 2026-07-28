
using System;
using Newtonsoft.Json;

namespace jjkWebFunctions2.Model
{
    public class MetricTotal
    {
        /*
    "id": "DAY",
    "TotalBucket": 20240510,
    "LastUpdateDateTime": "2024-05-10T20:59:48.5792462-04:00",
    "TotalValue": "11.40",
    "AmpMaxValue": null,
    "WattMaxValue": null,
        */
        public string id { get; set; }
        public string? TotalBucket { get; set; }
        public string? LastUpdateDateTime { get; set; }
        public float TotalValue { get; set; }
        public float AmpMaxValue { get; set; }
        public float WattMaxValue { get; set; }
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }

    }
}
