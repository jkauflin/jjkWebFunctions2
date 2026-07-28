
using System;
using Newtonsoft.Json;

namespace jjkWebFunctions2.Model
{
    public class GenvMetricPoint
    {
        /*
    "id": "bdf3d46f-3b28-477b-bdf8-d21a531850bb",
    "PointDay": 20250705,
    "PointDateTime": "2025-07-05 08:01:19",
    "PointDayTime": 25080119,
    "targetTemperature": 78,
    "currTemperature": 79.59,
    "airInterval": 0.7,
    "airDuration": 0.8,
    "heatInterval": 0.6,
    "heatDuration": 0.8,
    "waterDuration": 7,
    "waterInterval": 9,
    "lastWaterTs": "2025-07-04 23:59:55",
    "lastWaterSecs": 7,

      id: ID
  PointDay: Int
  PointDateTime: String
  PointDayTime: Int
  targetTemperature: Float
  currTemperature: Float
  airInterval: Float
  airDuration: Float
  heatInterval: Float
  heatDuration: Fl

  waterDuration: Int
  waterInterval: Int
  lastWaterTs: String
  lastWaterSecs: Int

        */
        public string id { get; set; }                  // guid "bdf3d46f-3b28-477b-bdf8-d21a531850bb"
        public int PointDay { get; set; }               // Partition key 20250705   /PointDay
        public string PointDateTime { get; set; }
        public int PointDayTime { get; set; }
        public float targetTemperature { get; set; }
        public float currTemperature { get; set; }
        public float airInterval { get; set; }
        public float airDuration { get; set; }
        public float heatInterval { get; set; }
        public float heatDuration { get; set; }
        public int waterDuration { get; set; }
        public int waterInterval { get; set; }
        public string lastWaterTs { get; set; }
        public int lastWaterSecs { get; set; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

}
