using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;

namespace FileMakerService.Classes.Options
{
    [Serializable]
    public static class OptionsLoader<CustomOptions> where CustomOptions : class
    {
        /// <summary>
        /// Полный путь к месту хранения файла JSON с параметрами
        /// </summary>
        private static string JsonFileName =>
            Path.Combine(AppContext.BaseDirectory, $"{typeof(CustomOptions).Name}.json");

        /// <summary>
        /// Полный путь к месту хранения файла XML с параметрами
        /// </summary>
        private static string XmlFileName =>
            Path.Combine(AppContext.BaseDirectory, $"{typeof(CustomOptions).Name}.xml");

        /// <summary>
        /// Формат даты для сериализации параметров в файл JSON
        /// </summary>
        private static string GetJsonDateTimeFormat => "yyyy-MM-dd";

        /// <summary>
        /// Сериализация параметров в файл JSON (сохранения параметров)
        /// </summary>
        public static void SaveOptionsToJson(CustomOptions options)
        {
            if (options != null)
            {
                JsonSerializerSettings settings = new JsonSerializerSettings();
                settings.Formatting = Formatting.Indented;
                settings.DateFormatString = GetJsonDateTimeFormat;
                string text = JsonConvert.SerializeObject(options, settings);
                File.WriteAllText(JsonFileName, text);
            }
        }

        /// <summary>
        /// Сериализация параметров в файл XML (сохранения параметров)
        /// </summary>
        public static void SaveOptionsToXml(CustomOptions options)
        {
            if (options != null)
            {
                XmlSerializer serializer = new XmlSerializer(typeof(CustomOptions));
                using (FileStream stream = new FileStream(XmlFileName, FileMode.Create))
                {
                    serializer.Serialize(stream, options);
                }
            }
        }

        /// <summary>
        /// Десериализация параметров из файла JSON (чтение параметров)
        /// </summary>
        public static CustomOptions? LoadOptionsFromJson()
        {
            if (!File.Exists(JsonFileName))
                return null;

            JsonSerializerSettings settings = new JsonSerializerSettings();
            settings.DateFormatString = GetJsonDateTimeFormat;
            string text = File.ReadAllText(JsonFileName);
            return JsonConvert.DeserializeObject<CustomOptions>(text, settings);
        }

        /// <summary>
        /// Десериализация параметров из файла XML (чтение параметров)
        /// </summary>
        public static CustomOptions? LoadOptionsFromXml()
        {
            if (!File.Exists(XmlFileName))
                return null;

            XmlSerializer serializer = new XmlSerializer(typeof(CustomOptions));
            using (FileStream stream = new FileStream(XmlFileName, FileMode.OpenOrCreate))
            {
                return serializer.Deserialize(stream) as CustomOptions;
            }
        }
    }
}
