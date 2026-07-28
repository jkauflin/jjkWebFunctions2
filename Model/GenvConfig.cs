
using System;
using Newtonsoft.Json;

namespace jjkWebFunctions2.Model
{
    public class GenvConfig
    {
        public string id { get; set; }                  // Just "1"
        public int ConfigId { get; set; }               // Partition key (1)
        public string configDesc { get; set; }
        public string daysToGerm { get; set; }
        public int daysToBloom { get; set; }
        public string germinationStart { get; set; }
        public string plantingDate { get; set; }
        public string harvestDate { get; set; }
        public string cureDate { get; set; }
        public string productionDate { get; set; }
        public int logMetricInterval { get; set; }
        public bool loggingOn { get; set; }
        public bool selfieOn { get; set; }
        public bool commandRequestOn { get; set; }
        public float targetTemperature { get; set; }
        public float currTemperature { get; set; }
        public float airInterval { get; set; }
        public float airDuration { get; set; }
        public float heatInterval { get; set; }
        public float heatDuration { get; set; }
        public float waterInterval { get; set; }
        public float waterDuration { get; set; }
        public float lightDuration { get; set; }
        public string notes { get; set; }
        public int s0day { get; set; }
        public int s0waterDuration { get; set; }
        public int s0waterInterval { get; set; }
        public int s1day { get; set; }
        public int s1waterDuration { get; set; }
        public int s1waterInterval { get; set; }
        public int s2day { get; set; }
        public int s2waterDuration { get; set; }
        public int s2waterInterval { get; set; }
        public int s3day { get; set; }
        public int s3waterDuration { get; set; }
        public int s3waterInterval { get; set; }
        public int s4day { get; set; }
        public int s4waterDuration { get; set; }
        public int s4waterInterval { get; set; }
        public int s5day { get; set; }
        public int s5waterDuration { get; set; }
        public int s5waterInterval { get; set; }
        public int s6day { get; set; }
        public int s6waterDuration { get; set; }
        public int s6waterInterval { get; set; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

}
