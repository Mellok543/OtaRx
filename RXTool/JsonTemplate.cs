using System;
using System.Linq;
using System.Text.Json;

namespace RxTool
{
    public static class JsonTemplate
    {
        // Берём template (JsonElement) и заменяем "uid":"$UID6" на реальный массив из 6 чисел.
        public static string BuildBindJson(JsonElement template, int[] uid6)
        {
            if (uid6 == null || uid6.Length != 6) throw new ArgumentException("UID must be length 6");
            if (uid6.Any(x => x < 0 || x > 255)) throw new ArgumentException("UID values must be 0..255");

            // Превращаем template в строку, затем делаем целевую замену безопасно:
            // ищем точную строку "$UID6" и заменяем на [..]
            var raw = template.GetRawText();

            var uidJson = "[" + string.Join(",", uid6) + "]";

            // В template это должно быть "uid":"$UID6" или где-то "$UID6"
            // меняем только значение строки "$UID6"
            raw = raw.Replace("\"$UID6\"", uidJson);

            return raw;
        }
    }
}