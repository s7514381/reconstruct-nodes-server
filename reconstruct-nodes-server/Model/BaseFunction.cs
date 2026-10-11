using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ReconstructNodesServer.Library.Attributes;
using ReconstructNodesServer.Library.Utility;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public static class BaseFunction
{
    public static IEnumerable<T> GetEnumItems<T>() where T : Enum
    {
        return Enum.GetValues(typeof(T)).Cast<T>();
    }

    public static int GetSplitLength(string pArray, char pSplit = ',')
    {
        int val = 0;
        if (!string.IsNullOrEmpty(pArray))
        {
            val = pArray.Split(pSplit).Length;
        }
        return val;
    }

    public static object? GetFieldValue(this object pObject, string pField)
    {
        PropertyInfo? Info = pObject.GetType().GetProperty(pField);
        return Info?.GetValue(pObject);
    }

    public static void SetFieldValue(this object pObject, string pField, object val)
    {
        PropertyInfo? Info = pObject.GetType().GetProperty(pField);
        Info?.SetValue(pObject, val);
    }

    public static void SetForeignKeyValue<TModel, TForeign>(this object pObject, object val)
    {
        string parentField = CommonUtility.GetForeignKeyField<TModel, TForeign>();
        if (string.IsNullOrEmpty(parentField)) { return; }

        PropertyInfo? Info = typeof(TModel).GetProperty(parentField);
        Info?.SetValue(pObject, val);
    }

    public static bool CheckInfoCanSetValue(object pObject, string infoName)
    {
        PropertyInfo? info = pObject.GetType().GetProperty(infoName);

        if (info == null) { return false; }
        if (info.GetValue(pObject) == null) { return false; }
        if (!info.CanWrite || !info.CanRead) { return false; }
        if (info.IsList()) { return false; }

        return true;
    }

    public static T Map<T>(this object pObject) where T : class, new()
    {
        T model = new T();
        PropertyInfo[] propertyInfos = model.GetType().GetProperties();

        foreach (var info in propertyInfos)
        {
            PropertyInfo? targetInfo = pObject.GetType().GetProperty(info.Name);

            if (targetInfo != null && CheckInfoCanSetValue(pObject, info.Name)) { info.SetValue(model, targetInfo.GetValue(pObject)); }
        }
        return model;
    }

    public static bool IsList(this PropertyInfo propertyInfo)
    {
        // 检查属性是否是泛型类型
        if (propertyInfo.PropertyType.IsGenericType)
        {
            // 获取泛型类型定义
            Type genericType = propertyInfo.PropertyType.GetGenericTypeDefinition();

            // 检查是否是 List<T> 或其他集合类型
            return genericType == typeof(List<>) ||
                   typeof(IEnumerable<>).IsAssignableFrom(genericType);
        }

        return false;
    }

    public static void DataCopyBy(this object source, object target)
    {
        if (target == null) { return; }

        PropertyInfo[] sourcePropertyInfos = source.GetType().GetProperties();
        PropertyInfo[] targetPropertyInfos = target.GetType().GetProperties();

        List<string> intersect = sourcePropertyInfos.Select(x => x.Name).Intersect(targetPropertyInfos.Select(x => x.Name)).ToList();

        foreach (var name in intersect)
        {
            PropertyInfo? info = target.GetType().GetProperty(name);
            info?.SetValue(source, info.GetValue(target));
        }
    }

    public static string GetDateString(string dateTime, string format)
    {
        return dateTime.ConvertDateFormat(format);
    }

    public static string ConvertDateFormat(this string dateString, string format)
    {
        if (string.IsNullOrEmpty(dateString)) { return dateString; }
        return DateTime.TryParse(dateString, out DateTime datetime) ? datetime.ToString(format) : dateString;
    }

    public static string GetArrayStringField(string arrayString, int arrayIndex, char split = ',')
    {
        string result = "";
        string[] Array = arrayString.Split(split);

        if (Array.Length >= arrayIndex + 1)
        {
            result = Array[arrayIndex];
        }
        return result;
    }

    public static DateTime GetChDateTime(DateTime? d = null)
    {
        if (d.HasValue) { return GetChDateTime(d.Value); }
        else { return GetChDateTime(DateTime.Now); }
    }

    public static DateTime GetChDateTime(DateTime d)
    {
        try { return d.AddYears(-1911); }
        catch { return d; }
    }

    public static string GetDateTimeCHString(this DateTime dateTime, bool IsConvertToChDateTime = false)
    {
        if (IsConvertToChDateTime) { dateTime = GetChDateTime(dateTime); }
        return dateTime.Year.ToString() + "年" + dateTime.Month.ToString() + "月" + dateTime.Day.ToString() + "日";
    }

    public static string GetDateTimeCHString(this DateTime? nullableDateTime, bool IsConvertToChDateTime = false)
    {
        string result = "";
        if (nullableDateTime.HasValue)
        {
            DateTime dateTime = (DateTime)nullableDateTime;
            result = dateTime.GetDateTimeCHString(IsConvertToChDateTime);
        }
        return result;
    }

    public static string GetWeekDayName(DayOfWeek dayOfWeek)
    {
        string WeekDay = "";
        switch (dayOfWeek)
        {
            case DayOfWeek.Monday:
                WeekDay = "星期一"; break;
            case DayOfWeek.Tuesday:
                WeekDay = "星期二"; break;
            case DayOfWeek.Wednesday:
                WeekDay = "星期三"; break;
            case DayOfWeek.Thursday:
                WeekDay = "星期四"; break;
            case DayOfWeek.Friday:
                WeekDay = "星期五"; break;
            case DayOfWeek.Saturday:
                WeekDay = "星期六"; break;
            case DayOfWeek.Sunday:
                WeekDay = "星期日"; break;
        }
        return WeekDay;
    }

    public static string StringFill(string paraString, int digits, string fillChar)
    {
        while (paraString.Length < digits) { paraString = fillChar + paraString; }
        return paraString;
    }

    public static bool IsValidEmail(string strIn)
    {
        return Regex.IsMatch(strIn, @"^([\w+\-.]+)@((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.)|(([\w-]+\.)+))([a-zA-Z]{2,4}|[0-9]{1,3})(\]?)$");
    }

    public static bool IsPhone(string strIn)
    {
        //有些縣市前綴有3碼或4碼的，先簡單做
        //return Regex.IsMatch(strIn, @"^\d{2}-\d{8}$");
        return strIn.Contains("-");
    }

    public static List<Guid> ToGuidList(this string value)
    {
        List<Guid> result = new List<Guid>();

        if (!string.IsNullOrEmpty(value))
        {
            foreach (string s in value.Split(','))
            {
                if (Guid.TryParse(s, out Guid guid)) { result.Add(guid); }
            }
        }
        return result;
    }

    public static bool ModelFieldValidate(object model, string fieldName)
    {
        PropertyInfo? Info = model.GetType().GetProperty(fieldName);
        if (Info == null) { return false; }

        bool isNullable = false;
        Type type = Info.PropertyType;

        if (type.IsGenericType &&
            type.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            isNullable = true;
            type = type.GetGenericArguments()[0];
        }
        bool hasValue = CheckHasValueByType(type.Name, isNullable, Info.GetValue(model));
        return hasValue;
    }

    public static bool CheckHasValueByType(string typeName, bool isNullable, object? value)
    {
        bool hasValue = false;

        switch (typeName)
        {
            case "String":
                hasValue = !string.IsNullOrEmpty((string?)value);
                break;
            case "Int32":
                if (isNullable) { hasValue = ((int?)value).HasValue; }
                else { hasValue = true; }
                break;
            case "Decimal":
                if (isNullable) { hasValue = ((decimal?)value).HasValue; }
                else { hasValue = true; }
                break;
            case "DateTime":
                if (isNullable) { hasValue = ((DateTime?)value).HasValue; }
                else { hasValue = true; }
                break;
            case "Boolean":
                if (isNullable) { hasValue = ((bool?)value).HasValue; }
                else { hasValue = true; }
                break;
            case "Byte":
                if (isNullable) { hasValue = ((byte?)value).HasValue; }
                else { hasValue = true; }
                break;
            case "Guid":
                if (isNullable) { hasValue = ((Guid?)value).HasValue; }
                else { hasValue = value is Guid guid && guid != Guid.Empty; }
                break;
            default:
                hasValue = value != null;
                break;
        }
        return hasValue;
    }

    public static void AddRange<T>(this ICollection<T> target, IEnumerable<T> source)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));
        if (source == null)
            throw new ArgumentNullException(nameof(source));
        foreach (var element in source)
            target.Add(element);
    }

    public static void AddTask(this List<Task> tasks, Func<Task> func)
    {
        tasks.Add(Task.Run(async () => {
            await func();
        }));
    }

    public static CancellationTokenSource AddSourceTask(this List<Task> tasks, Func<Task> func, CancellationTokenSource? source = null)
    {
        if (source == null) { source = new CancellationTokenSource(); }
        CancellationToken token = source.Token;

        tasks.Add(Task.Run(async () => {
            await func();
        }, token));

        return source;
    }

    public static string GetVerificationCode() { return Guid.NewGuid().ToString().Replace("-", "").Substring(0, 6).ToUpper(); }

    public static async Task SpeedDebugAsync(Func<Task> func, [CallerMemberName] string? propertyName = null)
    {
        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();

        await func();

        stopwatch.Stop();
        Console.WriteLine($@"{propertyName}: {stopwatch.ElapsedMilliseconds} 毫秒");
    }
    public static void SpeedDebug(Action func, [CallerMemberName] string? propertyName = null)
    {
        Stopwatch stopwatch = new Stopwatch();
        stopwatch.Start();

        func();

        stopwatch.Stop();
        Console.WriteLine($@"{propertyName}: {stopwatch.ElapsedMilliseconds} 毫秒");
    }


    /// <summary>
    /// 先不用
    /// </summary>
    /// <param name="newObject"></param>
    /// <param name="targetObject"></param>
    private static void Ma2p(this object newObject, object targetObject)
    {
        PropertyInfo[] propertyInfos = newObject.GetType().GetProperties();

        foreach (var info in propertyInfos)
        {
            PropertyInfo? targetInfo = targetObject.GetType().GetProperty(info.Name);

            if (targetInfo != null)
            {
                object? targetValue = targetInfo.GetValue(targetObject);

                if (targetInfo.IsList())
                {
                    var targetList = targetInfo.GetValue(targetObject) as IEnumerable<object>;
                    if (targetList != null)
                    {
                        var targetType = info.PropertyType.GetGenericArguments()[0];
                        var modelList = (IList?)Activator.CreateInstance(typeof(List<>).MakeGenericType(targetType));

                        foreach (var item in targetList)
                        {
                            var mappedItem = Activator.CreateInstance(targetType);
                            mappedItem?.Ma2p(item);
                            modelList?.Add(mappedItem);
                        }
                        info.SetValue(newObject, modelList);
                    }
                }
                else { info.SetValue(newObject, targetInfo.GetValue(targetObject)); }
            }
        }
    }

}
