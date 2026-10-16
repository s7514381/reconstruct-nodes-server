using System.Reflection;
using System.Text.RegularExpressions;

public static class BaseFunction
{
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
        if (propertyInfo.PropertyType.IsGenericType)
        {
            Type genericType = propertyInfo.PropertyType.GetGenericTypeDefinition();
            return genericType == typeof(List<>) ||
                   typeof(IEnumerable<>).IsAssignableFrom(genericType);
        }

        return false;
    }

    public static bool IsValidEmail(string strIn)
    {
        return Regex.IsMatch(strIn, @"^([\w+\-.]+)@((\[[0-9]{1,3}\.[0-9]{1,3}\.[0-9]{1,3}\.)|(([\w-]+\.)+))([a-zA-Z]{2,4}|[0-9]{1,3})(\]?)$");
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

    public static string GetVerificationCode() { return Guid.NewGuid().ToString().Replace("-", "").Substring(0, 6).ToUpper(); }
}
