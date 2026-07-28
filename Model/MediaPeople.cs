using Newtonsoft.Json;

namespace jjkWebFunctions2.Model
{
    public class MediaPeople
    {
        public string id { get; set; }                      // String of MediaPeopleId
        public int MediaPeopleId { get; set; }              // partitionKey
        public string PeopleName { get; set; }              // name of the person
        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }
    }
}
