using System;

namespace ReconstructNodesServer.Library.Attributes
{
    /// <summary>
    /// 檔案路徑型態的欄位，主要用途是異動紀錄裡面要顯示圖片
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class RelationAttribute : Attribute
    {
        public string Name { get; set; }
        public RelationAttribute(string name) { Name = name; }

    }
}
