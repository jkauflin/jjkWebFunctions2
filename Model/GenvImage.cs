
using System;
using Newtonsoft.Json;

namespace jjkWebFunctions2.Model
{
    public class GenvImage
    {
        public string id { get; set; }                  // Just "1"
        public int PointDay { get; set; }               // Partition key (1)
        public DateTime PointDateTime { get; set; }         
        public int PointDayTime { get; set; }         
        public string ImgData { get; set; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

}
