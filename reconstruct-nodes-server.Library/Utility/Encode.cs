using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text;

namespace ReconstructNodesServer.Utility
{
    public static class Encode
    {
        private static readonly string datetimeFormat = "yyyyMMddHHmmss";

        public static string New() 
        {
            string result = "";
            string sdate = DateTime.Now.ToString(datetimeFormat);
            string str = Guid.NewGuid().ToString().Replace("-", "");

            result = $"{InsertStringAtIntervals(str, sdate, 1)}|{CalAnswer(str)}";
            return result;
        }

        public static bool Validate(string str, double seconds) 
        {
            if (string.IsNullOrEmpty(str)) { return false; }

            string[] split = str.Split('|');
            if (split.Length != 2) { return false; }

            (string originalStr, string extractedSdate) = DecodeString(split[0], datetimeFormat.Length, 1);
            if (!DateTime.TryParseExact(extractedSdate, datetimeFormat, null, System.Globalization.DateTimeStyles.None, out DateTime dateTime)) { return false; }
            if (dateTime.AddSeconds(seconds) < DateTime.Now) { return false; }

            if (int.TryParse(split[1], out int answer)
                && CalAnswer(originalStr) == answer)
            {
                return true;
            }
            else { return false; }
        }

        public static int CalAnswer(string str) 
        {
            int result = 0;

            int x, z = 0;
            foreach (char s in str) 
            {
                if (int.TryParse(s.ToString(), out x))
                {
                    for (int i = 0; i < z; i++) 
                    {
                        x = x * -1 * (i + 1);
                    }
                    z = 0;

                    result += x;
                }
                else
                {
                    z++;
                }
            }
            return result;
        }

        private static string InsertStringAtIntervals(string original, string insert, int interval)
        {
            int insertIndex = 0;
            int insertLength = insert.Length;
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < original.Length; i++)
            {
                sb.Append(original[i]);

                if ((i + 1) % interval == 0 && insertIndex < insertLength)
                {
                    sb.Append(insert[insertIndex]);
                    insertIndex++;
                }
            }

            return sb.ToString();
        }

        private static (string, string) DecodeString(string encoded, int sdateLength, int interval)
        {
            StringBuilder originalStr = new StringBuilder();
            StringBuilder extractedSdate = new StringBuilder();

            int sdateIndex = 0;

            for (int i = 0; i < encoded.Length; i++)
            {
                if ((i + 1) % (interval + 1) == 0 && sdateIndex < sdateLength)
                {
                    extractedSdate.Append(encoded[i]);  // 取出插入的 sdate 字元
                    sdateIndex++;
                }
                else
                {
                    originalStr.Append(encoded[i]);  // 保留原始 GUID 部分
                }
            }

            return (originalStr.ToString(), extractedSdate.ToString());
        }

    }
}
