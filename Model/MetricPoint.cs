
using System;
using Newtonsoft.Json;

namespace jjkWebFunctions2.Model
{
    public class MetricPoint
    {
/*
    "id": "dda008c9-9d73-4b0e-9d2c-876e89d23305",
    "PointDay": 20250727,
    "PointDateTime": "2025-07-27T10:00:04.3012544Z",
    "PointYearMonth": 202507,
    "PointDayTime": 25060004,
    "pvVolts": "0.000",
    "pvAmps": "0.000",
    "pvWatts": "1.329",
*/
        public string id { get; set; }
        public int PointDay { get; set; }
        public string? PointDateTime { get; set; }
        public int PointYearMonth { get; set; }
        public int PointDayTime { get; set; }
        public float pvVolts { get; set; }
        public float pvAmps { get; set; }
        public float pvWatts { get; set; }
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}
