using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Generic;
using System.Linq;

namespace Utilities
{
    public class Deserialization
    {
        public static IDictionary<string, object> DeserializeData(string data)
        {
            if (data == null)
                return new Dictionary<string, object>();

            Logging.Debug("Deserializing " + data);
            var values = JsonConvert.DeserializeObject<Dictionary<string, object>>(data);

            return DeserializeData(values);
        }

        private static IDictionary<string, object> DeserializeData(JToken data)
        {
            var dict = data.ToObject<Dictionary<string, object>>();
            return dict != null ? DeserializeData(dict) : null;
        }

        private static IDictionary<string, object> DeserializeData(IDictionary<string, object> data)
        {
            foreach (var key in data.Keys.ToArray())
            {
                var value = data[key];

                switch (value)
                {
                    case JObject _:
                        data[key] = DeserializeData(value as JObject);
                        break;
                    case JArray _:
                        data[key] = DeserializeData(value as JArray);
                        break;
                }
            }

            return data;
        }

        private static IList<object> DeserializeData(JArray data)
        {
            var list = data.ToObject<List<object>>();

            for (var i = 0; i < list.Count; i++)
            {
                var value = list[i];

                switch (value)
                {
                    case JObject _:
                        list[i] = DeserializeData(value as JObject);
                        break;
                    case JArray _:
                        list[i] = DeserializeData(value as JArray);
                        break;
                }
            }
            return list;
        }
    }
}
