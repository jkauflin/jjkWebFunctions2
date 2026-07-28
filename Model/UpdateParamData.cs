
using System;
using Newtonsoft.Json;

namespace jjkWebFunctions2.Model
{
    public class UpdateParamData
    {
        public int MediaFilterMediaType { get; set; }              

        public Item[] MediaInfoFileList { get; set; } 

        public int FileListIndex { get; set; }   

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }

    public class Item
    {
        public string id { get; set; }                      // make it a GUID (not Name)
        public string name { get; set; }                    // name of the file
        public string takenDateTime { get; set; }         
        public string categoryTags { get; set; }
        public string menuTags { get; set; }
        public string albumTags { get; set; }
        public string title { get; set; }
        public string description { get; set; }
        public string people { get; set; }              
        public bool toBeProcessed { get; set; }
        public bool selected { get; set; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }

    }
}
